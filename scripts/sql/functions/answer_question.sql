
-- ==============================================================================
-- QUESTION ANSWERING: Extract answers from document graph
-- ==============================================================================

-- Function: Answer a natural language question
CREATE OR REPLACE FUNCTION answer_question(
    question TEXT,
    context_relation_hash BYTEA DEFAULT NULL, -- Optional: limit to specific document
    max_answer_length INTEGER DEFAULT 3       -- Max number of compositions in answer
)
RETURNS TABLE (
    answer TEXT,
    confidence DOUBLE PRECISION,
    source_compositions BYTEA[]
)
LANGUAGE plpgsql STABLE
AS $$
DECLARE
    focus_word TEXT;
    query_centroid RECORD;
    candidate RECORD;
    answer_parts TEXT[];
    answer_hashes BYTEA[];
    total_relevance DOUBLE PRECISION := 0;
BEGIN
    -- Step 1: Parse question to find focus (entity/topic)
    -- Example: "What is the name of the Captain" → focus = "Captain"

    focus_word := (
        SELECT word
        FROM regexp_split_to_table(question, '\s+') AS word
        WHERE word ~ '^[A-Z]' OR word IN ('who', 'what', 'where', 'when', 'why', 'how')
        ORDER BY
            CASE
                WHEN word ~ '^[A-Z]' THEN 1  -- Prioritize capitalized (proper nouns)
                ELSE 2
            END,
            LENGTH(word) DESC
        LIMIT 1
    );

    -- Get centroid of focus word
    SELECT centroid_x, centroid_y, centroid_z, centroid_w
    INTO query_centroid
    FROM compositions
    WHERE LOWER(text) = LOWER(focus_word)
    LIMIT 1;

    IF NOT FOUND THEN
        RETURN QUERY SELECT 'No answer found'::TEXT, 0.0, ARRAY[]::BYTEA[];
        RETURN;
    END IF;

    -- Step 2: Find nearby compositions (potential answer components)
    FOR candidate IN
        SELECT
            c.hash,
            c.text,
            st_distance_s3(
                query_centroid.centroid_x,
                query_centroid.centroid_y,
                query_centroid.centroid_z,
                query_centroid.centroid_w,
                c.centroid_x,
                c.centroid_y,
                c.centroid_z,
                c.centroid_w
            ) AS distance,
            (1.0 / (1.0 + st_distance_s3(
                query_centroid.centroid_x,
                query_centroid.centroid_y,
                query_centroid.centroid_z,
                query_centroid.centroid_w,
                c.centroid_x,
                c.centroid_y,
                c.centroid_z,
                c.centroid_w
            ))) AS relevance
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
                0.2
            )
        ORDER BY
            relevance DESC
        LIMIT max_answer_length
    LOOP
        answer_parts := array_append(answer_parts, candidate.text);
        answer_hashes := array_append(answer_hashes, candidate.hash);
        total_relevance := total_relevance + candidate.relevance;
    END LOOP;

    -- Step 3: Construct answer
    IF array_length(answer_parts, 1) IS NULL THEN
        RETURN QUERY SELECT 'No answer found'::TEXT, 0.0, ARRAY[]::BYTEA[];
    ELSE
        RETURN QUERY SELECT
            array_to_string(answer_parts, ' ')::TEXT,
            (total_relevance / max_answer_length)::DOUBLE PRECISION,
            answer_hashes;
    END IF;
END;
$$;

COMMENT ON FUNCTION answer_question IS 'Answer natural language questions using geometric reasoning';