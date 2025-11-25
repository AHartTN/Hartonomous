-- ============================================================================
-- Traverse Relations (Graph Traversal)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION traverse_relations(
    p_start_atom_id BIGINT,
    p_relation_type TEXT DEFAULT NULL,
    p_max_depth INTEGER DEFAULT 5
)
RETURNS TABLE(
    depth INTEGER,
    atom_id BIGINT,
    canonical_text TEXT,
    relation_type TEXT,
    weight REAL,
    path TEXT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH RECURSIVE relation_graph AS (
        -- Base case: start atom
        SELECT 
            0 as depth,
            a.atom_id,
            a.canonical_text,
            ''::TEXT as relation_type,
            1.0::REAL as weight,
            a.canonical_text as path
        FROM atom a
        WHERE a.atom_id = p_start_atom_id
        
        UNION ALL
        
        -- Recursive case: follow relations
        SELECT 
            rg.depth + 1,
            a.atom_id,
            a.canonical_text,
            rt.canonical_text,
            ar.weight,
            rg.path || ' ? ' || rt.canonical_text || ' ? ' || a.canonical_text
        FROM relation_graph rg
        JOIN atom_relation ar ON ar.source_atom_id = rg.atom_id
        JOIN atom a ON a.atom_id = ar.target_atom_id
        JOIN atom rt ON rt.atom_id = ar.relation_type_id
        WHERE rg.depth < p_max_depth
          AND (p_relation_type IS NULL OR rt.canonical_text = p_relation_type)
    )
    SELECT * FROM relation_graph
    ORDER BY depth, weight DESC;
END;
$$;

COMMENT ON FUNCTION traverse_relations(BIGINT, TEXT, INTEGER) IS 
'Traverse semantic relation graph from start atom, optionally filtering by relation type.';
