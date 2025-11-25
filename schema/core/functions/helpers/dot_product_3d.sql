-- ============================================================================
-- Helper: Dot Product (3D)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Compute dot product of two 3D geometry points
-- ============================================================================

CREATE OR REPLACE FUNCTION dot_product_3d(p_a GEOMETRY, p_b GEOMETRY)
RETURNS REAL
LANGUAGE plpgsql IMMUTABLE
AS $$
BEGIN
    RETURN ST_X(p_a) * ST_X(p_b) + 
           ST_Y(p_a) * ST_Y(p_b) + 
           ST_Z(p_a) * ST_Z(p_b);
END;
$$;

COMMENT ON FUNCTION dot_product_3d(GEOMETRY, GEOMETRY) IS 
'Helper: compute dot product of two 3D points (used in Gram-Schmidt, projections).';
