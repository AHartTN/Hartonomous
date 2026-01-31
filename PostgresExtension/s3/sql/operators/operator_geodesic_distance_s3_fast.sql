DROP OPERATOR IF EXISTS hartonomous.<~> (geometry, geometry);
CREATE OPERATOR hartonomous.<~> (
    LEFTARG = geometry,
    RIGHTARG = geometry,
    PROCEDURE = hartonomous.geodesic_distance_s3_fast
);

COMMENT ON OPERATOR hartonomous.<~> (geometry, geometry) IS
'Approximate geodesic distance operator for POINTZM geometries on S³.';