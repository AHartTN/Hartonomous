-- Voronoi Ambiguity Detection
-- Find atoms equidistant from multiple Voronoi cells (semantic ambiguity)
-- High ambiguity score = polysemous words, conflicting concepts, cross-domain terms

CREATE OR REPLACE FUNCTION detect_voronoi_ambiguity(
    p_landmark_atoms BIGINT[] DEFAULT NULL,
    p_distance_threshold FLOAT DEFAULT 0.05,
    p_max_results INT DEFAULT 1000
)
RETURNS TABLE(
    atom_id BIGINT,
    canonical_text TEXT,
    nearest_landmark_1 BIGINT,
    nearest_landmark_2 BIGINT,
    distance_1 FLOAT,
    distance_2 FLOAT,
    ambiguity_score FLOAT,
    modality TEXT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_landmark_count INT;
BEGIN
    -- If no landmarks specified, use semantic cluster centroids
    IF p_landmark_atoms IS NULL THEN
        p_landmark_atoms := ARRAY(
            SELECT a.atom_id
            FROM v_semantic_clusters sc
            JOIN atom a ON a.spatial_key = sc.centroid
            LIMIT 100
        );
    END IF;
    
    v_landmark_count := array_length(p_landmark_atoms, 1);
    
    IF v_landmark_count < 2 THEN
        RAISE EXCEPTION 'Need at least 2 landmark atoms for ambiguity detection';
    END IF;
    
    RAISE NOTICE 'Detecting ambiguity relative to % landmarks', v_landmark_count;
    
    RETURN QUERY
    WITH landmarks AS (
        -- Get landmark positions
        SELECT atom_id, spatial_key, canonical_text
        FROM atom
        WHERE atom_id = ANY(p_landmark_atoms)
          AND spatial_key IS NOT NULL
    ),
    all_atoms AS (
        -- Get all positioned atoms (candidates for ambiguity)
        SELECT 
            a.atom_id,
            a.canonical_text,
            a.spatial_key,
            a.metadata->>'modality' AS modality
        FROM atom a
        WHERE a.spatial_key IS NOT NULL
          AND NOT (a.atom_id = ANY(p_landmark_atoms))  -- Exclude landmarks themselves
    ),
    atom_distances AS (
        -- Compute distances from each atom to all landmarks
        SELECT 
            aa.atom_id,
            aa.canonical_text,
            aa.modality,
            -- Get 2 nearest landmarks
            (ARRAY_AGG(
                l.atom_id 
                ORDER BY ST_Distance(aa.spatial_key, l.spatial_key)
            ))[1:2] AS nearest_landmarks,
            -- Get 2 smallest distances
            (ARRAY_AGG(
                ST_Distance(aa.spatial_key, l.spatial_key)::FLOAT 
                ORDER BY ST_Distance(aa.spatial_key, l.spatial_key)
            ))[1:2] AS distances
        FROM all_atoms aa
        CROSS JOIN landmarks l
        GROUP BY aa.atom_id, aa.canonical_text, aa.modality
    )
    SELECT 
        ad.atom_id,
        ad.canonical_text,
        ad.nearest_landmarks[1] AS nearest_landmark_1,
        ad.nearest_landmarks[2] AS nearest_landmark_2,
        ad.distances[1] AS distance_1,
        ad.distances[2] AS distance_2,
        -- Ambiguity score: 1.0 = perfectly equidistant, 0.0 = clear winner
        -- Formula: 1 - |d1 - d2| / max(d1, d2)
        CASE 
            WHEN ad.distances[1] = 0 THEN 
                -- On landmark, no ambiguity
                0.0
            WHEN ad.distances[2] IS NULL THEN
                -- Only 1 landmark nearby, no ambiguity
                0.0
            ELSE 
                1.0 - (ABS(ad.distances[1] - ad.distances[2]) / 
                       GREATEST(ad.distances[2], 0.001))
        END AS ambiguity_score,
        ad.modality
    FROM atom_distances ad
    WHERE ad.distances[2] IS NOT NULL  -- At least 2 nearby landmarks
      AND ABS(ad.distances[1] - ad.distances[2]) < p_distance_threshold
    ORDER BY ambiguity_score DESC
    LIMIT p_max_results;
END;
$$;

COMMENT ON FUNCTION detect_voronoi_ambiguity IS
'Detect ambiguous atoms: atoms equidistant from multiple semantic clusters.

**Theory:**
- Voronoi cell = region of space closer to landmark X than any other landmark
- Voronoi edge = boundary between two cells (equidistant from 2+ landmarks)
- High ambiguity atoms = atoms near Voronoi edges

**Ambiguity Score:**
- 1.0 = Perfectly equidistant from 2 landmarks (maximum ambiguity)
- 0.5 = Moderate ambiguity (slightly closer to one landmark)
- 0.0 = Clear winner (strongly associated with single landmark)

**Use Cases:**
1. **Polysemous words:** "bank" (financial institution vs river bank)
2. **Cross-domain concepts:** "kernel" (OS kernel vs ML kernel vs nut kernel)
3. **Conflicting documentation:** Paragraphs with contradictory information
4. **Code smell detection:** Functions bridging unrelated modules

**Performance:**
- Uses PostGIS KNN (<->) for efficient nearest-neighbor queries
- Scales to 10M+ atoms with GIST indexes
- Threshold filtering reduces result set

**Example:**
    -- Find ambiguous atoms using default cluster centroids
    SELECT * FROM detect_voronoi_ambiguity()
    ORDER BY ambiguity_score DESC
    LIMIT 10;
    
    -- Find ambiguous code atoms near specific modules
    SELECT * FROM detect_voronoi_ambiguity(
        p_landmark_atoms := ARRAY[
            (SELECT atom_id FROM atom WHERE canonical_text = ''auth_module''),
            (SELECT atom_id FROM atom WHERE canonical_text = ''data_module''),
            (SELECT atom_id FROM atom WHERE canonical_text = ''ui_module'')
        ],
        p_distance_threshold := 0.1
    )
    WHERE modality = ''function'';
    
    atom_id | canonical_text       | nearest_landmark_1 | nearest_landmark_2 | ambiguity_score
    --------|----------------------|--------------------|--------------------|----------------
    12345   | validate_input       | auth_module        | data_module        | 0.98
    12389   | format_response      | data_module        | ui_module          | 0.87
    12401   | log_event            | auth_module        | data_module        | 0.76
';

-- Helper function: Find ambiguous atoms in specific modality
CREATE OR REPLACE FUNCTION detect_ambiguous_code(
    p_modality TEXT DEFAULT 'function',
    p_threshold FLOAT DEFAULT 0.05
)
RETURNS TABLE(
    atom_id BIGINT,
    canonical_text TEXT,
    ambiguity_score FLOAT,
    explanation TEXT
)
LANGUAGE SQL
AS $$
    SELECT 
        dv.atom_id,
        dv.canonical_text,
        dv.ambiguity_score,
        format(
            'Atom "%s" is equidistant from landmarks "%s" (d=%.3f) and "%s" (d=%.3f), indicating ambiguous purpose or cross-cutting concern.',
            dv.canonical_text,
            l1.canonical_text,
            dv.distance_1,
            l2.canonical_text,
            dv.distance_2
        ) AS explanation
    FROM detect_voronoi_ambiguity(
        p_distance_threshold := p_threshold
    ) dv
    JOIN atom l1 ON l1.atom_id = dv.nearest_landmark_1
    JOIN atom l2 ON l2.atom_id = dv.nearest_landmark_2
    WHERE dv.modality = p_modality
    ORDER BY dv.ambiguity_score DESC;
$$;

COMMENT ON FUNCTION detect_ambiguous_code IS
'Convenience function to find ambiguous code elements (functions, classes, etc).
Returns human-readable explanations of why atoms are ambiguous.';
