-- Pure spatial inference functions
-- NO external AI - intelligence IS the database geometry

-- Task decomposition via spatial similarity
-- Finds subtasks by searching lower Z-levels near parent task
CREATE OR REPLACE FUNCTION task_decompose(
    parent_task_id BYTEA,
    max_subtasks INTEGER DEFAULT 10
) RETURNS TABLE (
    subtask_id BYTEA,
    subtask_x DOUBLE PRECISION,
    subtask_y DOUBLE PRECISION,
    subtask_z DOUBLE PRECISION,
    distance DOUBLE PRECISION
) AS $$
BEGIN
    -- Find atoms at Z-1 level near parent
    RETURN QUERY
    WITH parent AS (
        SELECT geom, ST_Z(geom) as parent_z
        FROM atom
        WHERE atom_id = parent_task_id
    )
    SELECT 
        a.atom_id,
        ST_X(a.geom)::DOUBLE PRECISION,
        ST_Y(a.geom)::DOUBLE PRECISION,
        ST_Z(a.geom)::DOUBLE PRECISION,
        ST_3DDistance(a.geom, p.geom)::DOUBLE PRECISION as dist
    FROM atom a, parent p
    WHERE ST_Z(a.geom) = p.parent_z - 1
      AND a.atom_id != parent_task_id
    ORDER BY a.geom <-> p.geom
    LIMIT max_subtasks;
END;
$$ LANGUAGE plpgsql STABLE;

-- Skill retrieval by analogy
-- Given context atoms, find procedurally similar composition sequences
CREATE OR REPLACE FUNCTION skill_retrieve(
    context_atoms BYTEA[],
    k INTEGER DEFAULT 5
) RETURNS TABLE (
    skill_id BYTEA,
    similarity_score DOUBLE PRECISION,
    skill_modality INTEGER
) AS $$
BEGIN
    -- Compute centroid of context
    -- Find composition atoms near that centroid
    RETURN QUERY
    WITH context_centroid AS (
        SELECT ST_Centroid(ST_Collect(geom)) as center
        FROM atom
        WHERE atom_id = ANY(context_atoms)
    )
    SELECT 
        a.atom_id,
        1.0 / (1.0 + ST_3DDistance(a.geom, c.center))::DOUBLE PRECISION as score,
        a.modality
    FROM atom a, context_centroid c
    WHERE a.atom_class = 1  -- Compositions only
      AND a.modality = 3     -- Skill modality
    ORDER BY a.geom <-> c.center
    LIMIT k;
END;
$$ LANGUAGE plpgsql STABLE;

-- Analogy search: A is to B as C is to ?
-- Spatial vector arithmetic in semantic space
CREATE OR REPLACE FUNCTION analogy_search(
    a_id BYTEA,
    b_id BYTEA,
    c_id BYTEA,
    k INTEGER DEFAULT 10
) RETURNS TABLE (
    d_id BYTEA,
    d_x DOUBLE PRECISION,
    d_y DOUBLE PRECISION,
    d_z DOUBLE PRECISION,
    analogy_score DOUBLE PRECISION
) AS $$
BEGIN
    -- Compute vector A→B
    -- Apply to C to get target point
    -- Find nearest atoms to target
    RETURN QUERY
    WITH vectors AS (
        SELECT 
            a.geom as a_geom,
            b.geom as b_geom,
            c.geom as c_geom,
            ST_SetSRID(ST_MakePoint(
                ST_X(b.geom) - ST_X(a.geom) + ST_X(c.geom),
                ST_Y(b.geom) - ST_Y(a.geom) + ST_Y(c.geom),
                ST_Z(b.geom) - ST_Z(a.geom) + ST_Z(c.geom),
                ST_M(b.geom) - ST_M(a.geom) + ST_M(c.geom)
            ), 0) as target
        FROM 
            atom a,
            atom b,
            atom c
        WHERE a.atom_id = analogy_search.a_id
          AND b.atom_id = analogy_search.b_id
          AND c.atom_id = analogy_search.c_id
    )
    SELECT 
        d.atom_id,
        ST_X(d.geom)::DOUBLE PRECISION,
        ST_Y(d.geom)::DOUBLE PRECISION,
        ST_Z(d.geom)::DOUBLE PRECISION,
        1.0 / (1.0 + ST_3DDistance(d.geom, v.target))::DOUBLE PRECISION as score
    FROM atom d, vectors v
    WHERE d.atom_id NOT IN (a_id, b_id, c_id)
    ORDER BY d.geom <-> v.target
    LIMIT k;
END;
$$ LANGUAGE plpgsql STABLE;

-- Pattern completion via trajectory matching
-- Given partial sequence, find most likely continuations
CREATE OR REPLACE FUNCTION pattern_complete(
    partial_sequence BYTEA[],
    max_completions INTEGER DEFAULT 5
) RETURNS TABLE (
    completion_id BYTEA,
    next_atom_id BYTEA,
    confidence DOUBLE PRECISION
) AS $$
BEGIN
    -- Find compositions that START with partial sequence
    -- Return their next atoms weighted by frequency
    RETURN QUERY
    WITH sequence_matches AS (
        SELECT 
            ac.parent_atom_id as comp_id,
            ac.component_atom_id as next_atom,
            COUNT(*) OVER (PARTITION BY ac.component_atom_id) as freq
        FROM atom_compositions ac
        WHERE ac.sequence_index = array_length(partial_sequence, 1) + 1
          AND EXISTS (
              SELECT 1
              FROM atom_compositions ac2
              WHERE ac2.parent_atom_id = ac.parent_atom_id
                AND ac2.sequence_index <= array_length(partial_sequence, 1)
                AND ac2.component_atom_id = partial_sequence[ac2.sequence_index]
          )
    )
    SELECT DISTINCT
        comp_id,
        next_atom,
        (freq::DOUBLE PRECISION / MAX(freq) OVER ())::DOUBLE PRECISION as conf
    FROM sequence_matches
    ORDER BY conf DESC
    LIMIT max_completions;
END;
$$ LANGUAGE plpgsql STABLE;

-- Trajectory Fréchet distance for LINESTRING similarity
-- Finds sequences with similar "shape" in semantic space
CREATE OR REPLACE FUNCTION trajectory_similarity(
    query_composition_id BYTEA,
    k INTEGER DEFAULT 10
) RETURNS TABLE (
    similar_composition_id BYTEA,
    frechet_distance DOUBLE PRECISION,
    hausdorff_distance DOUBLE PRECISION
) AS $$
BEGIN
    -- Use PostGIS discrete Fréchet distance
    RETURN QUERY
    WITH query_geom AS (
        SELECT geom as qgeom
        FROM atom
        WHERE atom_id = query_composition_id
          AND atom_class = 1  -- Must be composition
    )
    SELECT 
        a.atom_id,
        ST_FrechetDistance(a.geom, q.qgeom)::DOUBLE PRECISION as frechet,
        ST_HausdorffDistance(a.geom, q.qgeom)::DOUBLE PRECISION as hausdorff
    FROM atom a, query_geom q
    WHERE a.atom_class = 1
      AND a.atom_id != query_composition_id
      AND ST_GeometryType(a.geom) = 'ST_LineString'
    ORDER BY ST_FrechetDistance(a.geom, q.qgeom)
    LIMIT k;
END;
$$ LANGUAGE plpgsql STABLE;

-- Hierarchical abstraction via Z-level traversal
-- Move up abstraction ladder from specific to general
CREATE OR REPLACE FUNCTION abstract_concept(
    concrete_atom_id BYTEA,
    levels INTEGER DEFAULT 1
) RETURNS TABLE (
    abstract_atom_id BYTEA,
    z_level DOUBLE PRECISION,
    semantic_distance DOUBLE PRECISION
) AS $$
BEGIN
    -- Find atoms at Z+levels that spatially contain this atom
    RETURN QUERY
    WITH concrete AS (
        SELECT geom, ST_Z(geom) as z
        FROM atom
        WHERE atom_id = concrete_atom_id
    )
    SELECT 
        a.atom_id,
        ST_Z(a.geom)::DOUBLE PRECISION,
        ST_3DDistance(a.geom, c.geom)::DOUBLE PRECISION
    FROM atom a, concrete c
    WHERE ST_Z(a.geom) >= c.z + levels
      AND ST_Z(a.geom) < c.z + levels + 1
    ORDER BY a.geom <-> c.geom
    LIMIT 10;
END;
$$ LANGUAGE plpgsql STABLE;

-- Concept refinement via downward traversal
-- Move down to more specific instances
CREATE OR REPLACE FUNCTION refine_concept(
    abstract_atom_id BYTEA,
    levels INTEGER DEFAULT 1
) RETURNS TABLE (
    specific_atom_id BYTEA,
    z_level DOUBLE PRECISION,
    semantic_distance DOUBLE PRECISION
) AS $$
BEGIN
    RETURN QUERY
    WITH abstract AS (
        SELECT geom, ST_Z(geom) as z
        FROM atom
        WHERE atom_id = abstract_atom_id
    )
    SELECT 
        a.atom_id,
        ST_Z(a.geom)::DOUBLE PRECISION,
        ST_3DDistance(a.geom, ab.geom)::DOUBLE PRECISION
    FROM atom a, abstract ab
    WHERE ST_Z(a.geom) <= ab.z - levels
      AND ST_Z(a.geom) > ab.z - levels - 1
    ORDER BY a.geom <-> ab.geom
    LIMIT 10;
END;
$$ LANGUAGE plpgsql STABLE;

COMMENT ON FUNCTION task_decompose IS 'Spatial task breakdown - finds subtasks at lower Z-levels';
COMMENT ON FUNCTION skill_retrieve IS 'Procedural memory retrieval via context similarity';
COMMENT ON FUNCTION analogy_search IS 'Vector arithmetic reasoning: A:B :: C:?';
COMMENT ON FUNCTION pattern_complete IS 'Sequence prediction from composition patterns';
COMMENT ON FUNCTION trajectory_similarity IS 'Finds similar process/behavior trajectories';
COMMENT ON FUNCTION abstract_concept IS 'Upward reasoning to general concepts';
COMMENT ON FUNCTION refine_concept IS 'Downward reasoning to specific instances';
