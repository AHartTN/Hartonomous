-- ==============================================================================
-- MULTI-HOP REASONING: Recursive ELO-weighted Graph Traversal
-- ==============================================================================

-- Function: Multi-hop reasoning (follow high-tension strands)
CREATE OR REPLACE FUNCTION multi_hop_reasoning(
    start_id UUID,
    max_hops INTEGER DEFAULT 3,
    min_elo DOUBLE PRECISION DEFAULT 1000.0
)
RETURNS TABLE (
    hop_depth INTEGER,
    path UUID[],
    final_composition_id UUID,
    cumulative_elo DOUBLE PRECISION,
    avg_elo DOUBLE PRECISION
)
LANGUAGE sql STABLE
AS $$
WITH RECURSIVE search_graph(
    current_id, 
    depth, 
    path, 
    total_elo,
    visited_ids
) AS (
    -- Base Case: Start Node
    SELECT
        start_id,
        0,
        ARRAY[start_id],
        0.0::DOUBLE PRECISION,
        ARRAY[start_id]
    
    UNION ALL

    -- Recursive Step: Traverse Relations
    SELECT
        target_seq.CompositionId,
        sg.depth + 1,
        sg.path || target_seq.CompositionId,
        sg.total_elo + rr.RatingValue,
        sg.visited_ids || target_seq.CompositionId
    FROM
        search_graph sg
    JOIN
        -- Find Relations containing the current composition
        RelationSequence rs_source ON rs_source.CompositionId = sg.current_id
    JOIN
        RelationRating rr ON rr.RelationId = rs_source.RelationId
    JOIN
        -- Find other compositions in the SAME relation (siblings)
        RelationSequence target_seq ON target_seq.RelationId = rs_source.RelationId
    WHERE
        sg.depth < max_hops
        AND rr.RatingValue >= min_elo
        AND target_seq.CompositionId != sg.current_id -- Don't stay on self
        AND NOT (target_seq.CompositionId = ANY(sg.visited_ids)) -- Cycle detection
)
SELECT
    depth,
    path,
    current_id AS final_composition_id,
    total_elo,
    CASE WHEN depth > 0 THEN total_elo / depth ELSE 0 END AS avg_elo
FROM
    search_graph
WHERE
    depth > 0
ORDER BY
    avg_elo DESC, depth ASC
LIMIT 100;
$$;

COMMENT ON FUNCTION multi_hop_reasoning IS 'Traverse the semantic graph via High-ELO Relations (Abductive Reasoning)';