-- Function: Find similar compositions by centroid proximity
CREATE OR REPLACE FUNCTION find_similar_compositions(
    target_hash BYTEA,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    hash BYTEA,
    text TEXT,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        c2.hash,
        c2.text,
        geodesic_distance_s3(
            c1.centroid_x, c1.centroid_y, c1.centroid_z, c1.centroid_w,
            c2.centroid_x, c2.centroid_y, c2.centroid_z, c2.centroid_w
        ) AS distance
    FROM
        compositions c1,
        compositions c2
    WHERE
        c1.hash = target_hash
        AND c2.hash != target_hash
    ORDER BY
        distance
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_similar_compositions IS 'Find compositions with similar centroids to a target';