-- Streaming reconstruction: Decompress composition atoms back into their constituents
-- Implements depth-first traversal through composition hierarchy

CREATE OR REPLACE FUNCTION reconstruct_composition(
    comp_atom_id BYTEA,
    max_depth INTEGER DEFAULT 10
) RETURNS TABLE (
    atom_id BYTEA,
    atom_class SMALLINT,
    modality SMALLINT,
    subtype VARCHAR,
    atomic_value BYTEA,
    sequence_order INTEGER,
    depth_level INTEGER
) LANGUAGE plpgsql AS $$
DECLARE
    rec RECORD;
    current_depth INTEGER := 0;
    seq_order INTEGER := 0;
BEGIN
    -- Check if target is actually a composition
    IF NOT EXISTS (
        SELECT 1 FROM atom WHERE atom.atom_id = comp_atom_id AND atom.atom_class = 1
    ) THEN
        RAISE EXCEPTION 'Atom % is not a composition', comp_atom_id;
    END IF;

    -- Recursive CTE to traverse composition tree
    RETURN QUERY
    WITH RECURSIVE composition_tree AS (
        -- Base case: Start with target composition
        SELECT
            c.component_atom_id,
            c.component_order,
            0 as depth
        FROM atom_compositions c
        WHERE c.parent_atom_id = comp_atom_id

        UNION ALL

        -- Recursive case: Expand child compositions
        SELECT
            c.component_atom_id,
            c.component_order,
            ct.depth + 1
        FROM composition_tree ct
        JOIN atom a ON a.atom_id = ct.component_atom_id
        JOIN atom_compositions c ON c.parent_atom_id = ct.component_atom_id
        WHERE a.atom_class = 1  -- Only expand compositions
          AND ct.depth < max_depth
    ),
    -- Get all atoms (constants and terminal compositions)
    terminal_atoms AS (
        SELECT
            ct.component_atom_id,
            ct.component_order,
            ct.depth
        FROM composition_tree ct
        LEFT JOIN atom a ON a.atom_id = ct.component_atom_id
        WHERE a.atom_class = 0  -- Constants only
           OR NOT EXISTS (
               SELECT 1 FROM atom_compositions c2
               WHERE c2.parent_atom_id = ct.component_atom_id
           )  -- Or leaf compositions
    )
    SELECT
        a.atom_id,
        a.atom_class,
        a.modality,
        a.subtype,
        a.atomic_value,
        ROW_NUMBER() OVER (ORDER BY ta.depth, ta.component_order) as sequence_order,
        ta.depth
    FROM terminal_atoms ta
    JOIN atom a ON a.atom_id = ta.component_atom_id
    ORDER BY ta.depth, ta.component_order;
END;
$$;

-- Stream reconstruction as text (for token sequences)
CREATE OR REPLACE FUNCTION reconstruct_text(
    comp_atom_id BYTEA
) RETURNS TEXT LANGUAGE plpgsql AS $$
DECLARE
    result TEXT := '';
    rec RECORD;
BEGIN
    FOR rec IN
        SELECT atomic_value
        FROM reconstruct_composition(comp_atom_id)
        WHERE modality = 1  -- Text modality
        ORDER BY sequence_order
    LOOP
        result := result || convert_from(rec.atomic_value, 'UTF8') || ' ';
    END LOOP;

    RETURN TRIM(result);
END;
$$;

-- Reconstruct image from pixel atoms
CREATE OR REPLACE FUNCTION reconstruct_image_pixels(
    comp_atom_id BYTEA,
    OUT width INTEGER,
    OUT height INTEGER,
    OUT pixel_data BYTEA
) LANGUAGE plpgsql AS $$
DECLARE
    pixel_count INTEGER;
    pixels BYTEA := '';
BEGIN
    -- Get image dimensions from metadata
    SELECT
        (metadata->>'width')::INTEGER,
        (metadata->>'height')::INTEGER
    INTO width, height
    FROM atom
    WHERE atom_id = comp_atom_id;

    -- Reconstruct pixel sequence
    SELECT string_agg(atomic_value, ''::BYTEA ORDER BY sequence_order)
    INTO pixels
    FROM reconstruct_composition(comp_atom_id)
    WHERE modality = 2;  -- Image modality

    pixel_data := pixels;
END;
$$;

-- Two-stage spatial filtering: coarse GiST then fine Fréchet
CREATE OR REPLACE FUNCTION trajectory_search(
    query_traj GEOMETRY,
    radius DOUBLE PRECISION DEFAULT 1.0,
    k INTEGER DEFAULT 10,
    frechet_threshold DOUBLE PRECISION DEFAULT 0.5
) RETURNS TABLE (
    atom_id BYTEA,
    frechet_distance DOUBLE PRECISION,
    hausdorff_distance DOUBLE PRECISION
) LANGUAGE plpgsql AS $$
BEGIN
    -- Stage 1: Coarse filter using GiST index (bounding box)
    RETURN QUERY
    WITH candidates AS (
        SELECT
            a.atom_id,
            a.geom
        FROM atom a
        WHERE a.atom_class = 1  -- Compositions only
          AND ST_DWithin(a.geom, query_traj, radius)
        ORDER BY a.geom <-> query_traj
        LIMIT k * 10  -- Over-fetch for Stage 2
    )
    -- Stage 2: Fine filter using Fréchet distance
    SELECT
        c.atom_id,
        ST_FrechetDistance(c.geom, query_traj) as frechet_distance,
        ST_HausdorffDistance(c.geom, query_traj) as hausdorff_distance
    FROM candidates c
    WHERE ST_FrechetDistance(c.geom, query_traj) <= frechet_threshold
    ORDER BY frechet_distance
    LIMIT k;
END;
$$;

-- Pattern mining: Find frequent sub-trajectories
CREATE OR REPLACE FUNCTION find_trajectory_patterns(
    min_support INTEGER DEFAULT 5,
    min_length INTEGER DEFAULT 3,
    max_length INTEGER DEFAULT 10
) RETURNS TABLE (
    pattern_atoms BYTEA[],
    support_count INTEGER,
    avg_frechet_similarity DOUBLE PRECISION
) LANGUAGE plpgsql AS $$
BEGIN
    -- Use sliding window over all compositions to find recurring patterns
    RETURN QUERY
    WITH composition_sequences AS (
        SELECT
            parent_atom_id,
            array_agg(component_atom_id ORDER BY component_order) as seq
        FROM atom_compositions
        GROUP BY parent_atom_id
    ),
    patterns AS (
        SELECT
            seq[i:i+len-1] as pattern,
            COUNT(*) as support
        FROM composition_sequences,
             generate_series(min_length, max_length) as len,
             generate_series(1, array_length(seq, 1) - len + 1) as i
        GROUP BY pattern
        HAVING COUNT(*) >= min_support
    )
    SELECT
        p.pattern,
        p.support::INTEGER,
        AVG(similarity)::DOUBLE PRECISION
    FROM patterns p
    CROSS JOIN LATERAL (
        SELECT ST_FrechetDistance(
            (SELECT geom FROM atom WHERE atom_id = ANY(p.pattern) LIMIT 1),
            (SELECT geom FROM atom WHERE atom_id = ANY(p.pattern) OFFSET 1 LIMIT 1)
        ) as similarity
    ) s
    GROUP BY p.pattern, p.support
    ORDER BY p.support DESC, avg_frechet_similarity DESC;
END;
$$;

COMMENT ON FUNCTION reconstruct_composition IS 'Depth-first traversal to decompress composition hierarchy';
COMMENT ON FUNCTION reconstruct_text IS 'Reconstruct text from token composition';
COMMENT ON FUNCTION trajectory_search IS 'Two-stage spatial search: GiST coarse filter + Fréchet fine filter';
COMMENT ON FUNCTION find_trajectory_patterns IS 'Discover frequent sub-trajectories across all compositions';
