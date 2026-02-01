-- s3_is_valid: Check if a POINTZM lies on the unit 3-sphere S³

CREATE OR REPLACE FUNCTION hartonomous.s3_is_valid(
    g geometry(POINTZM, 0),
    tolerance double precision DEFAULT 1e-10
)
RETURNS boolean
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    WITH comps AS (
        SELECT ST_X(g) AS x, ST_Y(g) AS y, ST_Z(g) AS z, ST_M(g) AS w
    )
    SELECT ABS(SQRT(x*x + y*y + z*z + w*w) - 1.0) < tolerance
    FROM comps;
$$;

COMMENT ON FUNCTION hartonomous.s3_is_valid(geometry, double precision) IS
'Returns true if the POINTZM geometry lies on the unit 3-sphere S³ (within tolerance).
A valid S³ point has x² + y² + z² + w² = 1.';
