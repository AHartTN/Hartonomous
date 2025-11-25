-- ============================================================================
-- Recalculate Landmarks
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Heavy atoms anchor semantic space - must be geometrically accurate
-- ============================================================================

CREATE OR REPLACE FUNCTION recalculate_landmarks(
    p_threshold BIGINT DEFAULT 1000000
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_count BIGINT;
BEGIN
    UPDATE atom
    SET spatial_key = compute_spatial_position(atom_id)
    WHERE reference_count > p_threshold;
    
    GET DIAGNOSTICS v_count = ROW_COUNT;
    
    RAISE NOTICE 'Recalculated % landmark atoms', v_count;
    
    RETURN v_count;
END;
$$;

COMMENT ON FUNCTION recalculate_landmarks(BIGINT) IS 
'Recalculate spatial positions for heavy/important atoms (landmarks).';
