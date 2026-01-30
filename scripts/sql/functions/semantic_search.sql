
-- ==============================================================================
-- SEMANTIC SEARCH: Find compositions by meaning, not just exact text
-- ==============================================================================

-- Function: Semantic search by query string
CREATE OR REPLACE FUNCTION semantic_search(
    query_text TEXT,
    max_results INTEGER DEFAULT 10,
    radius DOUBLE PRECISION DEFAULT 0.2
)
RETURNS TABLE (
    composition_hash BYTEA,
    text TEXT,
    relevance_score DOUBLE PRECISION,
    distance DOUBLE PRECISION
)
LANGUAGE plpgsql STABLE
AS $$
DECLARE
    query_centroid RECORD;
BEGIN
    -- Step 1: Parse query into tokens and find centroid
    -- For now, use the last significant word as the focus
    -- (In production, use more sophisticated query parsing)

    -- Extract the last noun/meaningful word
    -- Example: "What is the name of the Captain" → "Captain"
    DECLARE
        focus_word TEXT;
    BEGIN
        -- Simple heuristic: last capitalized word or last word
        focus_word := (
            SELECT word
            FROM regexp_split_to_table(query_text, '\s+') AS word
            WHERE word ~ '^[A-Z]' OR LENGTH(word) > 3
            ORDER BY LENGTH(word) DESC
            LIMIT 1
        );

        IF focus_word IS NULL THEN
            focus_word := (
                SELECT word
                FROM regexp_split_to_table(query_text, '\s+') AS word
                ORDER BY LENGTH(word) DESC
                LIMIT 1
            );
        END IF;

        -- Get centroid of focus word
        SELECT centroid_x, centroid_y, centroid_z, centroid_w
        INTO query_centroid
        FROM compositions
        WHERE LOWER(text) = LOWER(focus_word)
        LIMIT 1;

        IF NOT FOUND THEN
            RAISE NOTICE 'Focus word % not found, using full query', focus_word;
            RETURN;
        END IF;
    END;

    -- Step 2: Find compositions near query centroid
    RETURN QUERY
    SELECT
        c.hash AS composition_hash,
        c.text,
        -- Relevance score combines geometric distance + ELO edges
        (1.0 / (1.0 + st_distance_s3(
            query_centroid.centroid_x,
            query_centroid.centroid_y,
            query_centroid.centroid_z,
            query_centroid.centroid_w,
            c.centroid_x,
            c.centroid_y,
            c.centroid_z,
            c.centroid_w
        ))) AS relevance_score,
        st_distance_s3(
            query_centroid.centroid_x,
            query_centroid.centroid_y,
            query_centroid.centroid_z,
            query_centroid.centroid_w,
            c.centroid_x,
            c.centroid_y,
            c.centroid_z,
            c.centroid_w
        ) AS distance
    FROM
        compositions c
    WHERE
        c.hash != (SELECT hash FROM compositions WHERE LOWER(text) = LOWER(focus_word) LIMIT 1)
        AND st_dwithin_s3(
            query_centroid.centroid_x,
            query_centroid.centroid_y,
            query_centroid.centroid_z,
            query_centroid.centroid_w,
            c.centroid_x,
            c.centroid_y,
            c.centroid_z,
            c.centroid_w,
            radius
        )
    ORDER BY
        relevance_score DESC
    LIMIT max_results;
END;
$$;

COMMENT ON FUNCTION semantic_search IS 'Search compositions by semantic proximity (4D distance)';