CREATE OR REPLACE FUNCTION hartonomous.normalize_pointzm_s3(
    g geometry(POINTZM, 0)
)
RETURNS geometry(POINTZM, 0)
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    WITH comps AS (
        SELECT ST_X(g) AS x, ST_Y(g) AS y, ST_Z(g) AS z, ST_M(g) AS w
    ), mag AS (
        SELECT SQRT(x*x + y*y + z*z + w*w) AS r FROM comps
    )
    SELECT
        CASE WHEN r = 0 THEN g
        ELSE ST_SetSRID(
            ST_MakePoint(x / r, y / r, z / r, w / r),
            0
        )::geometry(POINTZM, 0)
        END
    FROM comps, mag;
$$;

COMMENT ON FUNCTION hartonomous.normalize_pointzm_s3(geometry) IS
'Normalize a POINTZM geometry to lie on the unit 3-sphere S³.';
