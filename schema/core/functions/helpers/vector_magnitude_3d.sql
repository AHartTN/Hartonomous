-- ============================================================================
-- Helper: Vector Magnitude (3D)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Compute magnitude (length) of 3D geometry vector
-- ============================================================================

CREATE OR REPLACE FUNCTION vector_magnitude_3d(p_v GEOMETRY)
RETURNS REAL
LANGUAGE plpgsql IMMUTABLE
AS $$
BEGIN
    RETURN SQRT(
        ST_X(p_v) * ST_X(p_v) + 
        ST_Y(p_v) * ST_Y(p_v) + 
        ST_Z(p_v) * ST_Z(p_v)
    );
END;
$$;

COMMENT ON FUNCTION vector_magnitude_3d(GEOMETRY) IS 
'Helper: compute magnitude (length) of 3D vector.';
