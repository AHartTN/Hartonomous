
-- ==============================================================================
-- SEMANTIC CLUSTERING: Phonetic and structural similarity
-- ==============================================================================

-- Function: Find phonetically similar compositions
CREATE OR REPLACE FUNCTION phonetic_search(
    query_text TEXT,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    composition_hash BYTEA,
    text TEXT,
    phonetic_similarity DOUBLE PRECISION
)
LANGUAGE plpgsql STABLE
AS $$
BEGIN
    -- Use Fréchet distance as proxy for phonetic similarity
    -- Characters with similar structure cluster together in 4D
    -- Example: "King" near "Ding" (K and D are both plosives)

    RETURN QUERY
    SELECT
        c.hash,
        c.text,
        1.0 / (1.0 + st_frechet_distance_4d(
            (SELECT hash FROM compositions WHERE text = query_text LIMIT 1),
            c.hash
        )) AS phonetic_similarity
    FROM
        compositions c
    WHERE
        c.text != query_text
        AND LENGTH(c.text) BETWEEN LENGTH(query_text) - 2 AND LENGTH(query_text) + 2
    ORDER BY
        phonetic_similarity DESC
    LIMIT max_results;
END;
$$;

COMMENT ON FUNCTION phonetic_search IS 'Find phonetically similar words using 4D trajectory similarity';