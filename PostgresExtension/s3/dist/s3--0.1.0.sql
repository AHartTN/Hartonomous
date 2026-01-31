-- Generated from sql/s3--0.1.0.sql
-- s3--0.1.0.sql
-- Core function/operator/opclass registration for s3 extension

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
