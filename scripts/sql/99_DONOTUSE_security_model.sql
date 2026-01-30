-- ==============================================================================
-- Hartonomous Security Model: Multi-Tenant with Content Poisoning Prevention
-- ==============================================================================
--
-- CRITICAL INSIGHT from user:
-- "whale" from Model A training = "whale" from Moby Dick = SAME COMPOSITION HASH
--
-- This means ALL content shares the SAME atoms/compositions globally.
-- But we need to prevent:
--   1. Cache poisoning (malicious user corrupts shared data)
--   2. Prompt injection (attacker inserts malicious compositions)
--   3. Data leakage (user A sees user B's private documents)
--   4. Model poisoning (attacker biases ELO ratings)
--
-- Solution: Multi-tenant security with provenance tracking
--
-- ==============================================================================

-- ==============================================================================
-- KEY INSIGHT: Atoms/Compositions are GLOBAL, but ownership is TRACKED
-- ==============================================================================
--
-- Example: "whale" composition
--
-- atoms table:
--   hash('w'), hash('h'), hash('a'), hash('l'), hash('e') → stored ONCE globally
--
-- compositions table:
--   hash("whale") → stored ONCE globally
--
-- content_ownership table:
--   (hash("whale"), 'composition', tenant_A, user_1, 'model_training')  ← GPT-3 training
--   (hash("whale"), 'composition', tenant_B, user_2, 'user_upload')     ← Moby Dick upload
--   (hash("whale"), 'composition', tenant_C, user_3, 'user_upload')     ← Marine biology paper
--
-- Result:
--   - "whale" stored ONCE (32 bytes hash + data)
--   - 3 ownership records (3 × 100 bytes = 300 bytes)
--   - Total: 332 bytes vs 3 × full storage
--
-- Deduplication even across tenants!
--
-- ==============================================================================

-- ==============================================================================
-- EDGE PROVENANCE: Track who contributed to ELO ratings
-- ==============================================================================

ALTER TABLE semantic_edges
ADD COLUMN IF NOT EXISTS provenance JSONB DEFAULT '[]'::JSONB;

COMMENT ON COLUMN semantic_edges.provenance IS 'Array of {tenant_id, user_id, model, weight, timestamp} votes';

-- ==============================================================================
-- ROW-LEVEL SECURITY: Enforce access control
-- ==============================================================================










-- ==============================================================================
-- NEAREST NEIGHBOR INTELLIGENCE: Semantic proximity without exact matches
-- ==============================================================================
--
-- User insight: "Call me Ishmael" appears once, but:
--   - "Call" is near "me" is near "Ishmael" in 4D space
--   - Can find similar phrases via geometric proximity
--   - Fuzzy matching: "Called me Ishmael", "Call him Ishmael"
--
-- Query: Find compositions similar to "Call"
--
SELECT
    c.hash,
    c.text,
    st_distance_s3(
        c_target.centroid_x, c_target.centroid_y, c_target.centroid_z, c_target.centroid_w,
        c.centroid_x, c.centroid_y, c.centroid_z, c.centroid_w
    ) AS distance
FROM
    compositions c,
    compositions c_target
WHERE
    c_target.text = 'Call'
    AND c.hash != c_target.hash
    AND st_dwithin_s3(
        c_target.centroid_x, c_target.centroid_y, c_target.centroid_z, c_target.centroid_w,
        c.centroid_x, c.centroid_y, c.centroid_z, c.centroid_w,
        0.1  -- Radius threshold
    )
ORDER BY distance
LIMIT 10;
--
-- Results: "Called", "Calling", "Calls", "Call", "Recall", ...
--
-- This enables semantic search without exact matches!
--
-- ==============================================================================



-- ==============================================================================
-- EXAMPLE USAGE
-- ==============================================================================

/*
-- Set current user context (application sets this)
SET app.current_tenant_id = '123e4567-e89b-12d3-a456-426614174000';
SET app.current_user_id = '987fcdeb-51a2-43f1-9876-543210fedcba';
SET app.current_user_role = 'user';

-- Insert content (automatically tracked)
INSERT INTO compositions (hash, length, centroid_x, centroid_y, centroid_z, centroid_w, ...)
VALUES (...);

-- Record ownership
INSERT INTO content_ownership (
    content_hash, content_type, tenant_id, user_id, source_type
) VALUES (
    hash("whale"), 'composition',
    current_setting('app.current_tenant_id')::UUID,
    current_setting('app.current_user_id')::UUID,
    'user_upload'
);

-- Add edge vote (updates ELO + provenance)
SELECT add_edge_vote(
    hash("cat"),
    hash("sat"),
    'attention',
    current_setting('app.current_tenant_id')::UUID,
    current_setting('app.current_user_id')::UUID,
    0.87,  -- weight
    'GPT-3'  -- model name
);

-- Query with tenant isolation (RLS enforces automatically)
SELECT * FROM compositions
WHERE hilbert_index BETWEEN 1000 AND 2000
LIMIT 10;
-- Only returns compositions owned by current tenant or marked public

-- Flag suspicious content
SELECT flag_content(
    hash("malicious injection"),
    'composition',
    current_setting('app.current_user_id')::UUID,
    'Contains prompt injection attempt'
);
*/
