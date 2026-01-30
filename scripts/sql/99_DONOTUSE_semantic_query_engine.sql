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
-- PERFORMANCE INDEXES
-- ==============================================================================

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
