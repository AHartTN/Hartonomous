-- ==============================================================================
-- FUNCTION: geodesic_distance_s3
-- Calculates the geodesic (angular) distance between two points on S³.
-- ==============================================================================

CREATE OR REPLACE FUNCTION geodesic_distance_s3(
    a GEOMETRY(POINTZM, 0),
    b GEOMETRY(POINTZM, 0)
)
RETURNS DOUBLE PRECISION
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    SELECT ACOS(
        LEAST(
            1.0,
            GREATEST(
                -1.0,
                ST_X(a) * ST_X(b) +
                ST_Y(a) * ST_Y(b) +
                ST_Z(a) * ST_Z(b) +
                ST_M(a) * ST_M(b)
            )
        )
    );
$$;

COMMENT ON FUNCTION geodesic_distance_s3 IS
'Calculate the geodesic (angular) distance between two points on S³.';
