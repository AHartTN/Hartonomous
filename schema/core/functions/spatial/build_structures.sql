-- Build geometric structures from atom compositions
-- Populates atom_structure table with LINESTRING, POLYGON, TIN geometries

CREATE OR REPLACE FUNCTION build_trajectory_structures(
    p_modality TEXT DEFAULT 'function',
    p_limit INT DEFAULT 1000
)
RETURNS TABLE(
    structure_id BIGINT,
    root_atom_id BIGINT,
    canonical_text TEXT,
    trajectory_length FLOAT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH trajectories AS (
        SELECT 
            ac.parent_atom_id,
            a_parent.canonical_text,
            ST_MakeLine(
                ARRAY_AGG(
                    a_comp.spatial_key 
                    ORDER BY ac.sequence_index
                )
            ) AS trajectory_geom
        FROM atom_composition ac
        JOIN atom a_comp ON a_comp.atom_id = ac.component_atom_id
        JOIN atom a_parent ON a_parent.atom_id = ac.parent_atom_id
        WHERE a_comp.spatial_key IS NOT NULL
          AND a_parent.metadata->>'modality' = p_modality
        GROUP BY ac.parent_atom_id, a_parent.canonical_text
        HAVING COUNT(*) >= 2  -- At least 2 points for line
        LIMIT p_limit
    )
    INSERT INTO atom_structure (
        root_atom_id,
        semantic_shape,
        structure_type,
        metadata
    )
    SELECT 
        t.parent_atom_id,
        ST_GeometryCollection(t.trajectory_geom),
        'TRAJECTORY',
        jsonb_build_object(
            'modality', p_modality,
            'length', ST_Length(t.trajectory_geom),
            'point_count', ST_NPoints(t.trajectory_geom)
        )
    FROM trajectories t
    ON CONFLICT (root_atom_id, structure_type) DO UPDATE
    SET semantic_shape = EXCLUDED.semantic_shape,
        metadata = EXCLUDED.metadata,
        updated_at = now()
    RETURNING 
        structure_id,
        root_atom_id,
        (SELECT canonical_text FROM atom WHERE atom_id = root_atom_id),
        (metadata->>'length')::FLOAT;
END;
$$;

COMMENT ON FUNCTION build_trajectory_structures IS
'Build TRAJECTORY structures (LINESTRING) from atom compositions.

Use for:
- Function execution flows (sequence of operations)
- Sentences (word sequences with spatial progression)
- Narratives (document reading order)

Example:
    SELECT * FROM build_trajectory_structures(''function'', 1000);
';

CREATE OR REPLACE FUNCTION build_scope_structures(
    p_modality TEXT DEFAULT 'class',
    p_limit INT DEFAULT 1000
)
RETURNS TABLE(
    structure_id BIGINT,
    root_atom_id BIGINT,
    canonical_text TEXT,
    scope_area FLOAT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH scopes AS (
        SELECT 
            ac.parent_atom_id,
            a_parent.canonical_text,
            ST_ConvexHull(
                ST_Collect(a_comp.spatial_key)
            ) AS scope_geom
        FROM atom_composition ac
        JOIN atom a_comp ON a_comp.atom_id = ac.component_atom_id
        JOIN atom a_parent ON a_parent.atom_id = ac.parent_atom_id
        WHERE a_comp.spatial_key IS NOT NULL
          AND a_parent.metadata->>'modality' = p_modality
        GROUP BY ac.parent_atom_id, a_parent.canonical_text
        HAVING COUNT(*) >= 3  -- At least 3 points for polygon
        LIMIT p_limit
    )
    INSERT INTO atom_structure (
        root_atom_id,
        semantic_shape,
        structure_type,
        metadata
    )
    SELECT 
        s.parent_atom_id,
        ST_GeometryCollection(s.scope_geom),
        'SCOPE',
        jsonb_build_object(
            'modality', p_modality,
            'area', ST_Area(s.scope_geom),
            'perimeter', ST_Perimeter(s.scope_geom),
            'point_count', ST_NPoints(s.scope_geom)
        )
    FROM scopes s
    ON CONFLICT (root_atom_id, structure_type) DO UPDATE
    SET semantic_shape = EXCLUDED.semantic_shape,
        metadata = EXCLUDED.metadata,
        updated_at = now()
    RETURNING 
        structure_id,
        root_atom_id,
        (SELECT canonical_text FROM atom WHERE atom_id = root_atom_id),
        (metadata->>'area')::FLOAT;
END;
$$;

COMMENT ON FUNCTION build_scope_structures IS
'Build SCOPE structures (POLYGON) from atom compositions.

Use for:
- Class boundaries (enclosing all methods)
- Paragraph boundaries (topic convex hull)
- Context boundaries (namespace spatial extent)

Example:
    SELECT * FROM build_scope_structures(''class'', 500);
';

CREATE OR REPLACE FUNCTION build_terrain_structures(
    p_parent_atom_ids BIGINT[] DEFAULT NULL,
    p_limit INT DEFAULT 100
)
RETURNS TABLE(
    structure_id BIGINT,
    root_atom_id BIGINT,
    canonical_text TEXT,
    triangle_count INT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH terrains AS (
        SELECT 
            ac.parent_atom_id,
            a_parent.canonical_text,
            ST_DelaunayTriangles(
                ST_Collect(a_comp.spatial_key)
            ) AS terrain_geom
        FROM atom_composition ac
        JOIN atom a_comp ON a_comp.atom_id = ac.component_atom_id
        JOIN atom a_parent ON a_parent.atom_id = ac.parent_atom_id
        WHERE a_comp.spatial_key IS NOT NULL
          AND (p_parent_atom_ids IS NULL OR ac.parent_atom_id = ANY(p_parent_atom_ids))
        GROUP BY ac.parent_atom_id, a_parent.canonical_text
        HAVING COUNT(*) >= 3  -- At least 3 points for triangulation
        LIMIT p_limit
    )
    INSERT INTO atom_structure (
        root_atom_id,
        semantic_shape,
        structure_type,
        metadata
    )
    SELECT 
        t.parent_atom_id,
        t.terrain_geom,
        'TERRAIN',
        jsonb_build_object(
            'triangle_count', ST_NumGeometries(t.terrain_geom),
            'area', ST_Area(t.terrain_geom)
        )
    FROM terrains t
    ON CONFLICT (root_atom_id, structure_type) DO UPDATE
    SET semantic_shape = EXCLUDED.semantic_shape,
        metadata = EXCLUDED.metadata,
        updated_at = now()
    RETURNING 
        structure_id,
        root_atom_id,
        (SELECT canonical_text FROM atom WHERE atom_id = root_atom_id),
        (metadata->>'triangle_count')::INT;
END;
$$;

COMMENT ON FUNCTION build_terrain_structures IS
'Build TERRAIN structures (TIN) from atom compositions using Delaunay triangulation.

Use for:
- Loss landscapes (elevation = loss, base = parameter space)
- Style surfaces (elevation = style score, base = embedding space)
- Embedding space topology

Example:
    -- Build terrain for specific models
    SELECT * FROM build_terrain_structures(
        ARRAY[
            (SELECT atom_id FROM atom WHERE canonical_text = ''GPT-4''),
            (SELECT atom_id FROM atom WHERE canonical_text = ''Llama-3'')
        ]
    );
';

CREATE OR REPLACE FUNCTION build_ambiguity_zones(
    p_threshold FLOAT DEFAULT 0.05,
    p_limit INT DEFAULT 500
)
RETURNS TABLE(
    structure_id BIGINT,
    root_atom_id BIGINT,
    canonical_text TEXT,
    ambiguity_score FLOAT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH ambiguous_atoms AS (
        SELECT 
            dv.atom_id,
            dv.canonical_text,
            dv.ambiguity_score,
            -- Buffer around atom position (ambiguity zone)
            ST_Buffer(a.spatial_key, 0.1) AS zone_geom
        FROM detect_voronoi_ambiguity(
            p_distance_threshold := p_threshold,
            p_max_results := p_limit
        ) dv
        JOIN atom a ON a.atom_id = dv.atom_id
        WHERE dv.ambiguity_score > 0.5  -- Only high ambiguity
    )
    INSERT INTO atom_structure (
        root_atom_id,
        semantic_shape,
        influence_zone,
        structure_type,
        metadata
    )
    SELECT 
        aa.atom_id,
        ST_GeometryCollection(ST_Point(ST_X(a.spatial_key), ST_Y(a.spatial_key))),
        aa.zone_geom,
        'AMBIGUITY_ZONE',
        jsonb_build_object(
            'ambiguity_score', aa.ambiguity_score,
            'modality', a.metadata->>'modality'
        )
    FROM ambiguous_atoms aa
    JOIN atom a ON a.atom_id = aa.atom_id
    ON CONFLICT (root_atom_id, structure_type) DO UPDATE
    SET semantic_shape = EXCLUDED.semantic_shape,
        influence_zone = EXCLUDED.influence_zone,
        metadata = EXCLUDED.metadata,
        updated_at = now()
    RETURNING 
        structure_id,
        root_atom_id,
        (SELECT canonical_text FROM atom WHERE atom_id = root_atom_id),
        (metadata->>'ambiguity_score')::FLOAT;
END;
$$;

COMMENT ON FUNCTION build_ambiguity_zones IS
'Build AMBIGUITY_ZONE structures from Voronoi edge detection.

Use for:
- Polysemous words (equidistant from multiple clusters)
- Cross-domain concepts (bridging unrelated areas)
- Conflicting documentation

Example:
    SELECT * FROM build_ambiguity_zones(0.1, 100);
';

-- Convenience function: Build all structures
CREATE OR REPLACE FUNCTION build_all_structures()
RETURNS TABLE(
    structure_type TEXT,
    count BIGINT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE NOTICE 'Building trajectories...';
    PERFORM build_trajectory_structures('function', 1000);
    
    RAISE NOTICE 'Building scopes...';
    PERFORM build_scope_structures('class', 500);
    
    RAISE NOTICE 'Building terrains...';
    PERFORM build_terrain_structures(NULL, 100);
    
    RAISE NOTICE 'Building ambiguity zones...';
    PERFORM build_ambiguity_zones(0.05, 500);
    
    RETURN QUERY
    SELECT 
        s.structure_type::TEXT,
        COUNT(*)
    FROM atom_structure s
    GROUP BY s.structure_type
    ORDER BY COUNT(*) DESC;
END;
$$;

COMMENT ON FUNCTION build_all_structures IS
'Build all structure types in one pass.
Useful for initial population or full rebuild.

Example:
    SELECT * FROM build_all_structures();
    
     structure_type   | count
    ------------------+-------
     TRAJECTORY       |  1000
     SCOPE            |   500
     AMBIGUITY_ZONE   |   234
     TERRAIN          |    87
';
