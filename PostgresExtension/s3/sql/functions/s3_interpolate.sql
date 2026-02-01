-- s3_interpolate: Spherical linear interpolation (SLERP) on S³

CREATE OR REPLACE FUNCTION hartonomous.s3_interpolate(
    a geometry(POINTZM, 0),
    b geometry(POINTZM, 0),
    t double precision  -- Interpolation parameter [0,1]
)
RETURNS geometry(POINTZM, 0)
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    WITH
    -- Extract components
    comps AS (
        SELECT
            ST_X(a) AS ax, ST_Y(a) AS ay, ST_Z(a) AS az, ST_M(a) AS aw,
            ST_X(b) AS bx, ST_Y(b) AS by, ST_Z(b) AS bz, ST_M(b) AS bw
    ),
    -- Compute dot product (cosine of angle)
    dot_calc AS (
        SELECT *, (ax*bx + ay*by + az*bz + aw*bw) AS dot
        FROM comps
    ),
    -- Clamp dot to [-1, 1] and compute angle
    angle_calc AS (
        SELECT *,
            ACOS(GREATEST(-1.0, LEAST(1.0, dot))) AS omega
        FROM dot_calc
    ),
    -- SLERP computation
    slerp AS (
        SELECT
            CASE
                WHEN omega < 1e-10 THEN
                    -- Points are nearly identical, use linear interpolation
                    (1.0 - t) * ax + t * bx
                ELSE
                    (SIN((1.0 - t) * omega) * ax + SIN(t * omega) * bx) / SIN(omega)
            END AS rx,
            CASE
                WHEN omega < 1e-10 THEN (1.0 - t) * ay + t * by
                ELSE (SIN((1.0 - t) * omega) * ay + SIN(t * omega) * by) / SIN(omega)
            END AS ry,
            CASE
                WHEN omega < 1e-10 THEN (1.0 - t) * az + t * bz
                ELSE (SIN((1.0 - t) * omega) * az + SIN(t * omega) * bz) / SIN(omega)
            END AS rz,
            CASE
                WHEN omega < 1e-10 THEN (1.0 - t) * aw + t * bw
                ELSE (SIN((1.0 - t) * omega) * aw + SIN(t * omega) * bw) / SIN(omega)
            END AS rw
        FROM angle_calc
    )
    SELECT ST_SetSRID(ST_MakePoint(rx, ry, rz, rw), 0)::geometry(POINTZM, 0)
    FROM slerp;
$$;

COMMENT ON FUNCTION hartonomous.s3_interpolate(geometry, geometry, double precision) IS
'Spherical linear interpolation (SLERP) between two points on S³.
Parameter t=0 returns point a, t=1 returns point b.
The interpolation follows the shortest geodesic path on S³.';
