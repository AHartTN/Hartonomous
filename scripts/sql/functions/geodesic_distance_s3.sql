-- ==============================================================================
-- FUNCTIONS: Geometric operations
-- ==============================================================================

-- Function: Calculate geodesic distance on S³ between two atoms
CREATE OR REPLACE FUNCTION geodesic_distance_s3(
    x1 DOUBLE PRECISION, y1 DOUBLE PRECISION, z1 DOUBLE PRECISION, w1 DOUBLE PRECISION,
    x2 DOUBLE PRECISION, y2 DOUBLE PRECISION, z2 DOUBLE PRECISION, w2 DOUBLE PRECISION
)
RETURNS DOUBLE PRECISION
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    SELECT ACOS(LEAST(1.0, GREATEST(-1.0, x1*x2 + y1*y2 + z1*z2 + w1*w2)));
$$;

COMMENT ON FUNCTION geodesic_distance_s3 IS 'Calculate geodesic distance (angle) between two points on S³';