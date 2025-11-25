-- ============================================================================
-- Reconstruct Atom from Components
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE OR REPLACE FUNCTION reconstruct_atom(p_atom_id BIGINT)
RETURNS TEXT
LANGUAGE plpgsql
AS $$
DECLARE
    v_result TEXT;
BEGIN
    -- Recursively reconstruct from components in sequence order
    SELECT string_agg(a.canonical_text, '' ORDER BY ac.sequence_index)
    INTO v_result
    FROM atom_composition ac
    JOIN atom a ON a.atom_id = ac.component_atom_id
    WHERE ac.parent_atom_id = p_atom_id;
    
    -- If no components, return canonical_text directly
    IF v_result IS NULL THEN
        SELECT canonical_text INTO v_result
        FROM atom WHERE atom_id = p_atom_id;
    END IF;
    
    RETURN v_result;
END;
$$;

COMMENT ON FUNCTION reconstruct_atom(BIGINT) IS 
'Reconstruct original text/content from atomic components (inverse of atomization).';
