-- ==============================================================================
-- PHOENETIC SEARCH: Search by sound-alike (Refactored for Atomized Schema)
-- ==============================================================================

CREATE OR REPLACE FUNCTION phoenetic_search(
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
        -- Use Soundex/Metaphone for blocking
        soundex(v.text) = soundex(query_text)
        OR metaphone(v.text, 4) = metaphone(query_text, 4)
    ORDER BY
        similarity DESC
    LIMIT max_results;
$$;

COMMENT ON FUNCTION phoenetic_search IS 'Search compositions by phonetic similarity (using reconstructed text)';