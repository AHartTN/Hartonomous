-- ============================================================================
-- Traverse Composition Hierarchy
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION traverse_composition(
    p_root_atom_id BIGINT,
    p_max_depth INTEGER DEFAULT 10
)
RETURNS TABLE(
    depth INTEGER,
    atom_id BIGINT,
    canonical_text TEXT,
    sequence_index BIGINT,
    path TEXT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH RECURSIVE composition_tree AS (
        -- Base case: root atom
        SELECT 
            0 as depth,
            a.atom_id,
            a.canonical_text,
            0::BIGINT as sequence_index,
            a.canonical_text as path
        FROM atom a
        WHERE a.atom_id = p_root_atom_id
        
        UNION ALL
        
        -- Recursive case: components
        SELECT 
            ct.depth + 1,
            a.atom_id,
            a.canonical_text,
            ac.sequence_index,
            ct.path || ' ? ' || a.canonical_text
        FROM composition_tree ct
        JOIN atom_composition ac ON ac.parent_atom_id = ct.atom_id
        JOIN atom a ON a.atom_id = ac.component_atom_id
        WHERE ct.depth < p_max_depth
    )
    SELECT * FROM composition_tree
    ORDER BY depth, sequence_index;
END;
$$;

COMMENT ON FUNCTION traverse_composition(BIGINT, INTEGER) IS 
'Traverse composition hierarchy depth-first, returning ordered tree structure.';
