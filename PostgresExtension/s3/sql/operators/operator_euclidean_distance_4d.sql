DROP OPERATOR IF EXISTS hartonomous.<#> (geometry, geometry);
CREATE OPERATOR hartonomous.<#> (
    LEFTARG = geometry,
    RIGHTARG = geometry,
    PROCEDURE = hartonomous.euclidean_distance_4d
);

COMMENT ON OPERATOR hartonomous.<#> (geometry, geometry) IS
'Euclidean distance operator for 4D POINTZM geometries.';