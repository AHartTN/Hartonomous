-- ============================================================================
-- Batch Spatial Positioning
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Efficiently compute positions for multiple atoms
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_spatial_positions_batch(p_atom_ids BIGINT[])
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_atom_id BIGINT;
    v_position GEOMETRY;
    v_count BIGINT := 0;
BEGIN
    FOREACH v_atom_id IN ARRAY p_atom_ids LOOP
        v_position := compute_spatial_position(v_atom_id);
        
        UPDATE atom
        SET spatial_key = v_position
        WHERE atom_id = v_atom_id;
        
        v_count := v_count + 1;
    END LOOP;
    
    RETURN v_count;
END;
$$;

COMMENT ON FUNCTION compute_spatial_positions_batch(BIGINT[]) IS 
'Batch update spatial positions for multiple atoms (performance optimization).';
