-- s3_dwithin: Range query for S³ geometry
-- Returns true if two points on S³ are within the given geodesic distance

CREATE OR REPLACE FUNCTION hartonomous.s3_dwithin(
    a geometry(POINTZM, 0),
    b geometry(POINTZM, 0),
    distance double precision
)
RETURNS boolean
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    SELECT hartonomous.geodesic_distance_s3(a, b) <= distance;
$$;

COMMENT ON FUNCTION hartonomous.s3_dwithin(geometry, geometry, double precision) IS
'Returns true if two POINTZM geometries on S³ are within the specified geodesic distance.
The distance is the angular distance in radians (0 to π).';
