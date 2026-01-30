-- Function: Find similar compositions by centroid proximity
CREATE OR REPLACE FUNCTION find_similar_compositions(
    target_id UUID,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    id UUID,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        c2.Id,
        geodesic_distance_s3(
            ST_X(p1.Centroid), ST_Y(p1.Centroid), ST_Z(p1.Centroid), ST_M(p1.Centroid),
            ST_X(p2.Centroid), ST_Y(p2.Centroid), ST_Z(p2.Centroid), ST_M(p2.Centroid)
        ) AS distance
    FROM
        Composition c1
    JOIN
        Physicality p1 ON c1.PhysicalityId = p1.Id
    JOIN
        Composition c2 ON c1.Id != c2.Id
    JOIN
        Physicality p2 ON c2.PhysicalityId = p2.Id
    WHERE
        c1.Id = target_id
    ORDER BY
        p2.Centroid <-> p1.Centroid
    LIMIT max_results;
$$;

COMMENT ON FUNCTION find_similar_compositions IS 'Find compositions with similar centroids to a target';