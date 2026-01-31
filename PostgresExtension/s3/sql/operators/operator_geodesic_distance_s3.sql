DROP OPERATOR IF EXISTS hartonomous.<=> (geometry, geometry);
CREATE OPERATOR hartonomous.<=> (
    LEFTARG = geometry,
    RIGHTARG = geometry,
    PROCEDURE = hartonomous.geodesic_distance_s3
);

COMMENT ON OPERATOR hartonomous.<=> (geometry, geometry) IS
'Geodesic distance operator for POINTZM geometries on S³.';