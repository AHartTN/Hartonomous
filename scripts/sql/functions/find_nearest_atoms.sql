-- Function: Find nearest atoms to a given 4D point
CREATE OR REPLACE FUNCTION find_nearest_atoms(
    target_x DOUBLE PRECISION,
    target_y DOUBLE PRECISION,
    target_z DOUBLE PRECISION,
    target_w DOUBLE PRECISION,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    hash BYTEA,
    codepoint INTEGER,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        a.hash,
        a.codepoint,
        geodesic_distance_s3(target_x, target_y, target_z, target_w,
                             a.s3_x, a.s3_y, a.s3_z, a.s3_w) AS distance
    FROM
        atoms a
    ORDER BY
        distance
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_nearest_atoms IS 'Find k-nearest atoms to a target 4D point on S³';