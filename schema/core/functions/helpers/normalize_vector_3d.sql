-- ============================================================================
-- Helper: Normalize Vector (3D)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Normalize 3D vector to unit length
-- ============================================================================

CREATE OR REPLACE FUNCTION normalize_vector_3d(p_v GEOMETRY)
RETURNS GEOMETRY
LANGUAGE plpgsql IMMUTABLE
AS $$
DECLARE
    v_mag REAL;
BEGIN
    v_mag := vector_magnitude_3d(p_v);
    
    IF v_mag < 0.001 THEN
        RETURN p_v;  -- Avoid division by zero
    END IF;
    
    RETURN ST_MakePoint(
        ST_X(p_v) / v_mag,
        ST_Y(p_v) / v_mag,
        ST_Z(p_v) / v_mag
    );
END;
$$;

COMMENT ON FUNCTION normalize_vector_3d(GEOMETRY) IS 
'Helper: normalize 3D vector to unit length.';
