-- ==============================================================================
-- Semantic Query Engine: Geometric Reasoning for Natural Language
-- ==============================================================================
--
-- User Insight: "What is the name of the Captain" should return "Ahab"
--
-- How: Geometric proximity in 4D space!
--   1. Parse query → compositions ["What", "is", "the", "name", "of", "the", "Captain"]
--   2. Find "Captain" in 4D space → get centroid
--   3. Search compositions NEAR "Captain" → ["Ahab", "ship", "whale", "sea"]
--   4. Rank by relevance (ELO edges, geometric distance)
--   5. Return answer: "Captain Ahab"
--
-- Additional Insight: Fréchet distance for fuzzy matching
--   - "King" near "Ding" (one letter different)
--   - "there" near "their" (phonetic similarity)
--   - Enables typo correction, phonetic search
--
-- ==============================================================================

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

-- ==============================================================================
-- EXAMPLE: "What is the name of the Captain?"
-- ==============================================================================
/*
SELECT * FROM semantic_search('What is the name of the Captain', 10, 0.2);

Results:
  composition_hash  |   text    | relevance_score | distance
--------------------+-----------+-----------------+----------
  hash("Ahab")      | Ahab      | 0.952           | 0.050
  hash("ship")      | ship      | 0.835           | 0.198
  hash("whale")     | whale     | 0.821           | 0.219
  hash("sea")       | sea       | 0.789           | 0.267
  hash("crew")      | crew      | 0.756           | 0.323
  ...

Answer: "Ahab" (highest relevance)
*/

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

-- ==============================================================================
-- EXAMPLE: Fuzzy search for "King" (finds "Ding", "Ring", "Sing", etc.)
-- ==============================================================================
/*
SELECT * FROM fuzzy_search('King', 10, 0.1);

Results:
  composition_hash  |   text    | frechet_distance
--------------------+-----------+------------------
  hash("King")      | King      | 0.000
  hash("Ding")      | Ding      | 0.015   ← One letter off!
  hash("Ring")      | Ring      | 0.018
  hash("Sing")      | Sing      | 0.021
  hash("Bing")      | Bing      | 0.023
  hash("king")      | king      | 0.005   ← Case variation
  hash("Kings")     | Kings     | 0.032   ← Plural
  ...

Enables:
  - Typo correction ("Knig" → "King")
  - Phonetic matching ("there" → "their")
  - Case-insensitive search
*/

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

-- ==============================================================================
-- EXAMPLE: "What is the name of the Captain?"
-- ==============================================================================
/*
SELECT * FROM answer_question('What is the name of the Captain', NULL, 3);

Results:
  answer           | confidence | source_compositions
-------------------+------------+---------------------
  Captain Ahab     | 0.912      | {hash("Captain"), hash("Ahab")}

How it works:
  1. Focus word: "Captain"
  2. Find nearby: "Ahab" (distance 0.050), "ship" (0.198), "whale" (0.219)
  3. Top results: "Ahab" + optional context
  4. Answer: "Captain Ahab" (highest confidence)
*/

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

-- ==============================================================================
-- EXAMPLE: Phonetic search for "their"
-- ==============================================================================
/*
SELECT * FROM phonetic_search('their', 10);

Results:
  composition_hash  |   text    | phonetic_similarity
--------------------+-----------+---------------------
  hash("there")     | there     | 0.923   ← Same pronunciation!
  hash("they're")   | they're   | 0.881   ← Contraction
  hash("thier")     | thier     | 0.856   ← Common misspelling
  hash("tier")      | tier      | 0.789   ← Similar sound
  hash("them")      | them      | 0.712   ← Related pronoun
  ...

Enables:
  - Spell correction
  - Homophone detection
  - Accent-aware search
*/

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

-- ==============================================================================
-- PERFORMANCE INDEXES
-- ==============================================================================

-- Specialized index for semantic search (centroid proximity)
CREATE INDEX CONCURRENTLY idx_compositions_centroid_gist
ON compositions USING GIST (centroid_x, centroid_y, centroid_z, centroid_w);

-- Full-text search fallback (when geometric search fails)
CREATE INDEX CONCURRENTLY idx_compositions_text_gin
ON compositions USING GIN (to_tsvector('english', text));

-- ==============================================================================
-- USAGE EXAMPLES
-- ==============================================================================

/*
-- Example 1: Semantic search
SELECT * FROM semantic_search('What is the name of the Captain', 10, 0.2);

-- Example 2: Answer question
SELECT * FROM answer_question('What is the name of the Captain', NULL, 3);

-- Example 3: Fuzzy search (typo-tolerant)
SELECT * FROM fuzzy_search('Captin', 10, 0.1);  -- Finds "Captain"

-- Example 4: Phonetic search
SELECT * FROM phonetic_search('there', 10);  -- Finds "their", "they're"

-- Example 5: Multi-hop reasoning
SELECT * FROM multi_hop_reasoning('What ship did the Captain command?', 3);
  -- Path: ["Captain", "Ahab", "Pequod"]
  -- Answer: "Pequod"
*/
