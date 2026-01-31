CREATE OR REPLACE FUNCTION hartonomous.geodesic_distance_s3(
    a geometry(POINTZM, 0),
    b geometry(POINTZM, 0)
)
RETURNS double precision
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'geodesic_distance_s3_c';

COMMENT ON FUNCTION hartonomous.geodesic_distance_s3(geometry, geometry) IS
'Calculate the geodesic (angular) distance between two points on S³ using the native C++ engine.';
