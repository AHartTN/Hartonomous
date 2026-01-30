
-- ==============================================================================
-- SEMANTIC SEARCH: Find compositions by geometric proximity on S³
-- ==============================================================================

-- Function: Semantic search by 4D coordinate (Centroid)
CREATE OR REPLACE FUNCTION semantic_search(
    query_x DOUBLE PRECISION,
    query_y DOUBLE PRECISION,
    query_z DOUBLE PRECISION,
    query_w DOUBLE PRECISION,
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
            query_x, query_y, query_z, query_w,
            ST_X(p.Centroid), ST_Y(p.Centroid), ST_Z(p.Centroid), ST_M(p.Centroid)
        ) AS distance
    FROM
        Physicality p
    JOIN
        Composition c ON c.PhysicalityId = p.Id
    ORDER BY
        -- Use Euclidean distance for fast GIST index sorting (approximates Geodesic order locally)
        p.Centroid <-> ST_SetSRID(ST_MakePoint(query_x, query_y, query_z, query_w), 0)
    LIMIT max_results;
$$;

COMMENT ON FUNCTION semantic_search IS 'Search compositions by geometric proximity (4D distance) on S³';