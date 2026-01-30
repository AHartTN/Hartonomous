
-- ==============================================================================
-- FUZZY MATCHING: Fréchet distance for typo tolerance
-- ==============================================================================

-- Function: Find compositions similar to a query (fuzzy match)
CREATE OR REPLACE FUNCTION fuzzy_search(
    query_text TEXT,
    max_results INTEGER DEFAULT 10,
    max_distance DOUBLE PRECISION DEFAULT 0.1
)
RETURNS TABLE (
    composition_hash BYTEA,
    text TEXT,
    frechet_distance DOUBLE PRECISION
)
LANGUAGE plpgsql STABLE
AS $$
DECLARE
    query_hash BYTEA;
BEGIN
    -- Try exact match first
    SELECT hash INTO query_hash
    FROM compositions
    WHERE LOWER(text) = LOWER(query_text)
    LIMIT 1;

    IF FOUND THEN
        -- Exact match exists, find similar trajectories
        RETURN QUERY
        SELECT
            c.hash AS composition_hash,
            c.text,
            st_frechet_distance_4d(query_hash, c.hash) AS frechet_distance
        FROM
            compositions c
        WHERE
            c.hash != query_hash
        ORDER BY
            frechet_distance
        LIMIT max_results;
    ELSE
        -- No exact match, fall back to semantic search
        RAISE NOTICE 'No exact match for %, using semantic search', query_text;
        RETURN QUERY
        SELECT
            s.composition_hash,
            s.text,
            s.distance AS frechet_distance
        FROM
            semantic_search(query_text, max_results, max_distance) s;
    END IF;
END;
$$;

COMMENT ON FUNCTION fuzzy_search IS 'Find compositions similar to query using Fréchet distance (typo-tolerant)';