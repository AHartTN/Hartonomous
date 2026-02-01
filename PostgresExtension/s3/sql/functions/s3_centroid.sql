-- s3_centroid: Aggregate function for computing the centroid on S³
-- Uses the normalized arithmetic mean (Karcher mean approximation)

-- State transition function
CREATE OR REPLACE FUNCTION hartonomous._s3_centroid_sfunc(
    state double precision[],
    g geometry(POINTZM, 0)
)
RETURNS double precision[]
LANGUAGE SQL IMMUTABLE
AS $$
    SELECT ARRAY[
        COALESCE(state[1], 0) + ST_X(g),
        COALESCE(state[2], 0) + ST_Y(g),
        COALESCE(state[3], 0) + ST_Z(g),
        COALESCE(state[4], 0) + ST_M(g),
        COALESCE(state[5], 0) + 1
    ];
$$;

-- Final function
CREATE OR REPLACE FUNCTION hartonomous._s3_centroid_ffunc(
    state double precision[]
)
RETURNS geometry(POINTZM, 0)
LANGUAGE SQL IMMUTABLE
AS $$
    WITH sums AS (
        SELECT state[1] AS sx, state[2] AS sy, state[3] AS sz, state[4] AS sw, state[5] AS n
    ),
    means AS (
        SELECT sx/n AS mx, sy/n AS my, sz/n AS mz, sw/n AS mw FROM sums
    ),
    normalized AS (
        SELECT
            SQRT(mx*mx + my*my + mz*mz + mw*mw) AS r,
            mx, my, mz, mw
        FROM means
    )
    SELECT
        CASE WHEN r = 0 THEN
            ST_SetSRID(ST_MakePoint(1, 0, 0, 0), 0)::geometry(POINTZM, 0)
        ELSE
            ST_SetSRID(ST_MakePoint(mx/r, my/r, mz/r, mw/r), 0)::geometry(POINTZM, 0)
        END
    FROM normalized;
$$;

-- Create the aggregate
DROP AGGREGATE IF EXISTS hartonomous.s3_centroid(geometry);
CREATE AGGREGATE hartonomous.s3_centroid(geometry(POINTZM, 0)) (
    SFUNC = hartonomous._s3_centroid_sfunc,
    STYPE = double precision[],
    FINALFUNC = hartonomous._s3_centroid_ffunc,
    INITCOND = '{0, 0, 0, 0, 0}'
);

COMMENT ON AGGREGATE hartonomous.s3_centroid(geometry) IS
'Computes the centroid (normalized mean) of a set of points on S³.
This is an approximation of the Fréchet mean, accurate for clustered points.';
