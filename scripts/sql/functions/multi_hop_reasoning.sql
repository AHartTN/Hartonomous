
-- ==============================================================================
-- MULTI-HOP REASONING: Follow semantic edges
-- ==============================================================================

-- Function: Multi-hop question answering (follow relationships)
CREATE OR REPLACE FUNCTION multi_hop_reasoning(
    query TEXT,
    max_hops INTEGER DEFAULT 3
)
RETURNS TABLE (
    path TEXT[],
    final_answer TEXT,
    path_confidence DOUBLE PRECISION
)
LANGUAGE plpgsql
AS $$
BEGIN
    -- Use A* pathfinding to traverse semantic graph
    -- Example: "What ship did the Captain command?"
    --   1. Find "Captain" → nearby "Ahab"
    --   2. Find "Ahab" → nearby "Pequod" (via semantic edges)
    --   3. Find "Pequod" → verify it's a "ship"
    --   4. Answer: "Pequod"

    -- This requires semantic edges with ELO ratings
    -- Implementation uses astar_pathfind() from postgis_spatial_functions.sql

    RETURN QUERY
    WITH reasoning_path AS (
        SELECT
            p.path_hash,
            p.total_cost,
            p.path_length
        FROM
            astar_pathfind(
                (SELECT hash FROM compositions WHERE text ~ query LIMIT 1),
                NULL, -- Goal: any high-ELO edge
                max_hops
            ) p
    )
    SELECT
        ARRAY(
            SELECT text FROM compositions
            WHERE hash = ANY(reasoning_path.path_hash)
        ) AS path,
        (SELECT text FROM compositions WHERE hash = reasoning_path.path_hash[array_length(reasoning_path.path_hash, 1)]) AS final_answer,
        (1.0 / (1.0 + reasoning_path.total_cost))::DOUBLE PRECISION AS path_confidence
    FROM
        reasoning_path;
END;
$$;

COMMENT ON FUNCTION multi_hop_reasoning IS 'Multi-hop reasoning using semantic graph traversal';