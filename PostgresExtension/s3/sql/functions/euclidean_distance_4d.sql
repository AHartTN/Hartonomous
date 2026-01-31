CREATE OR REPLACE FUNCTION hartonomous.euclidean_distance_4d(
    a geometry(POINTZM, 0),
    b geometry(POINTZM, 0)
)
RETURNS double precision
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    SELECT SQRT(
        (ST_X(a) - ST_X(b))^2 +
        (ST_Y(a) - ST_Y(b))^2 +
        (ST_Z(a) - ST_Z(b))^2 +
        (ST_M(a) - ST_M(b))^2
    );
$$;

COMMENT ON FUNCTION hartonomous.euclidean_distance_4d(geometry, geometry) IS
'Compute 4D Euclidean distance between two POINTZM geometries in R^4.';
