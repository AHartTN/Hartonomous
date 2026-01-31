-- ==============================================================================
-- SEMANTIC SEARCH: Find compositions by geometric proximity on S³
-- ==============================================================================

-- Function: Semantic search by 4D coordinate (Centroid Geometry)
CREATE OR REPLACE FUNCTION semantic_search_geometric(
    query_point GEOMETRY,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    composition_id UUID,
    distance DOUBLE PRECISION
)
LANGUAGE SQL STABLE
AS $$
    SELECT
        c.Id AS composition_id,
        geodesic_distance_s3(
            ST_X(query_point), ST_Y(query_point), ST_Z(query_point), ST_M(query_point),
            ST_X(p.Centroid), ST_Y(p.Centroid), ST_Z(p.Centroid), ST_M(p.Centroid)
        ) AS distance
    FROM
        Physicality p
    JOIN
        Composition c ON c.PhysicalityId = p.Id
    ORDER BY
        -- Use Euclidean distance for fast GIST index sorting (approximates Geodesic order locally)
        p.Centroid <-> query_point
    LIMIT max_results;
$$;

COMMENT ON FUNCTION semantic_search_geometric IS 'Search compositions by geometric proximity (4D distance) on S³';