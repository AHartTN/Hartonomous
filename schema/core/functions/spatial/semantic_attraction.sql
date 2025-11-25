-- ============================================================================
-- Semantic Attraction
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Inverse square law for semantic gravity
-- ============================================================================

CREATE OR REPLACE FUNCTION semantic_attraction(
    p_atom1 BIGINT,
    p_atom2 BIGINT
)
RETURNS REAL
LANGUAGE plpgsql
AS $$
DECLARE
    v_distance REAL;
    v_force REAL;
BEGIN
    SELECT ST_Distance(a1.spatial_key, a2.spatial_key)
    INTO v_distance
    FROM atom a1, atom a2
    WHERE a1.atom_id = p_atom1
      AND a2.atom_id = p_atom2;
    
    IF v_distance IS NULL THEN
        RETURN 0.0;
    END IF;
    
    -- Inverse square law (prevent division by zero)
    v_force := 1.0 / ((v_distance * v_distance) + 0.01);
    
    RETURN v_force;
END;
$$;

COMMENT ON FUNCTION semantic_attraction(BIGINT, BIGINT) IS 
'Compute semantic attraction force between two atoms (inverse square law).';
