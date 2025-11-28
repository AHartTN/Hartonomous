-- ============================================================================
-- Random Position Initialization
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Initialize atoms in bounded random positions when no neighbors exist
-- ============================================================================

CREATE OR REPLACE FUNCTION initialize_random_position()
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_x DOUBLE PRECISION;
    v_y DOUBLE PRECISION;
    v_z DOUBLE PRECISION;
    v_hilbert_index BIGINT;
BEGIN
    -- Initialize in bounded 3D space [-10, 10] for each dimension
    v_x := random() * 20 - 10;  -- X: [-10, 10]
    v_y := random() * 20 - 10;  -- Y: [-10, 10]
    v_z := random() * 20 - 10;  -- Z: [-10, 10]
    
    -- Compute Hilbert index for the random position
    v_hilbert_index := hilbert_index_3d(v_x, v_y, v_z, 10);
    
    -- Return POINTZM with M = Hilbert index
    RETURN ST_MakePoint(v_x, v_y, v_z, v_hilbert_index::DOUBLE PRECISION);
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION initialize_random_position() IS 
'Generate random 4D position (POINTZM) in bounded space [-10, 10]³.
M coordinate stores Hilbert index computed from (X,Y,Z).
Used for initializing atoms when no semantic neighbors exist yet.';
