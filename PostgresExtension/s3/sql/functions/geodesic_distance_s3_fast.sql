CREATE OR REPLACE FUNCTION hartonomous.geodesic_distance_s3_fast(
    a geometry(POINTZM, 0),
    b geometry(POINTZM, 0)
)
RETURNS double precision
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    SELECT 2.0 * ASIN(
        LEAST(
            1.0,
            0.5 * SQRT(
                (ST_X(a) - ST_X(b))^2 +
                (ST_Y(a) - ST_Y(b))^2 +
                (ST_Z(a) - ST_Z(b))^2 +
                (ST_M(a) - ST_M(b))^2
            )
        )
    );
$$;

COMMENT ON FUNCTION hartonomous.geodesic_distance_s3_fast(geometry, geometry) IS
'Fast approximate S³ geodesic distance using chordal approximation.';
