-- ============================================================================
-- Decompose Atom (Conservation of Reference)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Distribute parent's reference count to components
-- ============================================================================

CREATE OR REPLACE FUNCTION decompose_atom(p_parent_id BIGINT)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_parent_refcount BIGINT;
    v_component_count INT;
    v_increment REAL;
BEGIN
    SELECT reference_count INTO v_parent_refcount
    FROM atom WHERE atom_id = p_parent_id;
    
    IF v_parent_refcount IS NULL THEN
        RAISE EXCEPTION 'Atom % not found', p_parent_id;
    END IF;
    
    SELECT COUNT(*) INTO v_component_count
    FROM atom_composition WHERE parent_atom_id = p_parent_id;
    
    IF v_component_count = 0 THEN
        RETURN;  -- No components to distribute to
    END IF;
    
    v_increment := v_parent_refcount::REAL / v_component_count;
    
    -- Distribute reference count to components
    UPDATE atom
    SET reference_count = reference_count + v_increment::BIGINT
    WHERE atom_id IN (
        SELECT component_atom_id FROM atom_composition
        WHERE parent_atom_id = p_parent_id
    );
END;
$$;

COMMENT ON FUNCTION decompose_atom(BIGINT) IS 
'Decompose atom: distribute parent reference count to components (conservation law).';
