-- ==============================================================================
-- FUZZY SEARCH: Levenshtein/Trigram similarity (Refactored for Atomized Schema)
-- ==============================================================================

CREATE OR REPLACE FUNCTION fuzzy_search(
    query_text TEXT,
    max_results INTEGER DEFAULT 10
)
RETURNS TABLE (
    composition_id UUID,
    text TEXT,
    similarity DOUBLE PRECISION
)
LANGUAGE sql STABLE
AS $$
    SELECT
        v.composition_id,
        v.text,
        similarity(v.text, query_text) AS similarity
    FROM
        v_composition_text v
    WHERE
        -- Trigram index support if available on the view (materialized) or scan
        v.text % query_text
    ORDER BY
        similarity DESC
    LIMIT max_results;
$$;

COMMENT ON FUNCTION fuzzy_search IS 'Search compositions by fuzzy string matching (using reconstructed text)';