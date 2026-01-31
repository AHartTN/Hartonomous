-- ==============================================================================
-- ANSWER QUESTION: System 2 Reasoning via Graph Traversal
-- ==============================================================================

CREATE OR REPLACE FUNCTION answer_question(
    question_text TEXT,
    max_hops INTEGER DEFAULT 3
)
RETURNS TABLE (
    answer TEXT,
    confidence DOUBLE PRECISION,
    reasoning_path TEXT[]
)
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    start_node UUID;
BEGIN
    -- 1. Identify Entry Point (Focus Concept)
    -- Use fuzzy search to find the most relevant existing concept
    SELECT composition_id INTO start_node
    FROM fuzzy_search(question_text, 1);

    IF start_node IS NULL THEN
        RETURN QUERY SELECT 'I do not understand the concepts in this question.'::TEXT, 0.0::DOUBLE PRECISION, ARRAY[]::TEXT[];
        RETURN;
    END IF;

    -- 2. Execute Multi-Hop Reasoning (High-ELO traversal)
    RETURN QUERY
    WITH reasoning_chain AS (
        SELECT * FROM multi_hop_reasoning(start_node, max_hops)
        ORDER BY cumulative_elo DESC
        LIMIT 1
    )
    SELECT
        v.text AS answer,
        (1.0 - (1.0 / (1.0 + rc.cumulative_elo))) AS confidence, -- Sigmoid-like normalization of ELO
        (
            SELECT ARRAY_AGG(vp.text ORDER BY ordinal)
            FROM UNNEST(rc.path) WITH ORDINALITY AS p(id, ordinal)
            JOIN v_composition_text vp ON vp.composition_id = p.id
        ) AS reasoning_path
    FROM
        reasoning_chain rc
    JOIN
        v_composition_text v ON v.composition_id = rc.final_composition_id;
END;
$$;

COMMENT ON FUNCTION answer_question IS 'Answer questions by traversing the High-ELO Knowledge Graph';