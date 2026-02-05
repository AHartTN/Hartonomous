-- Generated from /home/ahart/Projects/Hartonomous/PostgresExtension/s3/sql/s3--0.1.0.sql
-- s3--0.1.0.sql
-- Core function/operator/opclass registration for s3 extension

-- Distance functions
-- Including functions/geodesic_distance_s3.sql
CREATE OR REPLACE FUNCTION hartonomous.geodesic_distance_s3(
    a geometry(POINTZM, 0),
    b geometry(POINTZM, 0)
)
RETURNS double precision
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'geodesic_distance_s3_c';

COMMENT ON FUNCTION hartonomous.geodesic_distance_s3(geometry, geometry) IS
'Calculate the geodesic (angular) distance between two points on S³ using the native C++ engine.';

-- Including functions/geodesic_distance_s3_fast.sql
CREATE OR REPLACE FUNCTION hartonomous.geodesic_distance_s3_fast(
    a geometry(POINTZM, 0),
    b geometry(POINTZM, 0)
)
RETURNS double precision
LANGUAGE SQL IMMUTABLE STRICT
AS $$
    SELECT 2.0 * ASIN(
        LEAST(
            1.0,
            0.5 * SQRT(
                (ST_X(a) - ST_X(b))^2 +
                (ST_Y(a) - ST_Y(b))^2 +
                (ST_Z(a) - ST_Z(b))^2 +
                (ST_M(a) - ST_M(b))^2
            )
        )
    );
$$;

COMMENT ON FUNCTION hartonomous.geodesic_distance_s3_fast(geometry, geometry) IS
'Fast approximate S³ geodesic distance using chordal approximation.';

-- Including functions/euclidean_distance_4d.sql
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


-- S³ geometry utilities
-- Including functions/normalize_pointzm_s3.sql
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

-- Including functions/s3_is_valid.sql
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

-- Including functions/s3_dwithin.sql
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

-- Including functions/s3_interpolate.sql
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

-- Including functions/s3_centroid.sql
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


-- GIST index support
-- Including functions/gist/gist_s3_support.sql
CREATE OR REPLACE FUNCTION hartonomous.gist_s3_consistent(internal, geometry, int4)
RETURNS bool
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_consistent';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_union(internal, internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_union';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_compress(internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_compress';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_decompress(internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_decompress';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_penalty(internal, internal, internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_penalty';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_picksplit(internal, internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_picksplit';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_same(internal, internal, internal)
RETURNS internal
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_same';

CREATE OR REPLACE FUNCTION hartonomous.gist_s3_distance(internal, geometry, int4)
RETURNS float8
LANGUAGE C IMMUTABLE STRICT
AS 's3', 'gist_s3_distance';


-- Operators
-- Including operators/operator_geodesic_distance_s3.sql
DROP OPERATOR IF EXISTS hartonomous.<=> (geometry, geometry);
CREATE OPERATOR hartonomous.<=> (
    LEFTARG = geometry,
    RIGHTARG = geometry,
    PROCEDURE = hartonomous.geodesic_distance_s3
);

COMMENT ON OPERATOR hartonomous.<=> (geometry, geometry) IS
'Geodesic distance operator for POINTZM geometries on S³.';
-- Including operators/operator_geodesic_distance_s3_fast.sql
DROP OPERATOR IF EXISTS hartonomous.<~> (geometry, geometry);
CREATE OPERATOR hartonomous.<~> (
    LEFTARG = geometry,
    RIGHTARG = geometry,
    PROCEDURE = hartonomous.geodesic_distance_s3_fast
);

COMMENT ON OPERATOR hartonomous.<~> (geometry, geometry) IS
'Approximate geodesic distance operator for POINTZM geometries on S³.';
-- Including operators/operator_euclidean_distance_4d.sql
DROP OPERATOR IF EXISTS hartonomous.<#> (geometry, geometry);
CREATE OPERATOR hartonomous.<#> (
    LEFTARG = geometry,
    RIGHTARG = geometry,
    PROCEDURE = hartonomous.euclidean_distance_4d
);

COMMENT ON OPERATOR hartonomous.<#> (geometry, geometry) IS
'Euclidean distance operator for 4D POINTZM geometries.';

-- Operator classes for indexing
-- Including opclass/gist_s3_ops.sql
CREATE OPERATOR CLASS hartonomous.gist_s3_ops
FOR TYPE geometry USING gist AS
    OPERATOR 1 hartonomous.<=> (geometry, geometry) FOR ORDER BY pg_catalog.float_ops,
    FUNCTION 1 hartonomous.gist_s3_consistent (internal, geometry, int4),
    FUNCTION 2 hartonomous.gist_s3_union (internal, internal),
    FUNCTION 3 hartonomous.gist_s3_compress (internal),
    FUNCTION 4 hartonomous.gist_s3_decompress (internal),
    FUNCTION 5 hartonomous.gist_s3_penalty (internal, internal, internal),
    FUNCTION 6 hartonomous.gist_s3_picksplit (internal, internal),
    FUNCTION 7 hartonomous.gist_s3_same (internal, internal, internal),
    FUNCTION 8 hartonomous.gist_s3_distance (internal, geometry, int4);
