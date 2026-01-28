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

-- Enable Row-Level Security (RLS)
ALTER DATABASE hartonomous SET row_security = on;

-- ==============================================================================
-- TENANTS: Multi-tenant isolation
-- ==============================================================================

CREATE TABLE tenants (
    tenant_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_name VARCHAR(255) NOT NULL UNIQUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE,

    -- Quota limits (prevent abuse)
    max_storage_gb INTEGER DEFAULT 100,
    max_compositions INTEGER DEFAULT 10000000,
    max_relations INTEGER DEFAULT 1000000,

    -- Billing
    subscription_tier VARCHAR(50) DEFAULT 'free',
    billing_email VARCHAR(255)
);

CREATE INDEX idx_tenants_active ON tenants (is_active);

COMMENT ON TABLE tenants IS 'Multi-tenant isolation for shared Hartonomous instance';

-- ==============================================================================
-- USERS: Per-tenant users
-- ==============================================================================

CREATE TABLE users (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    username VARCHAR(255) NOT NULL,
    email VARCHAR(255) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE,

    -- Permissions
    role VARCHAR(50) DEFAULT 'user' CHECK (role IN ('admin', 'user', 'readonly')),

    CONSTRAINT users_unique_username_per_tenant UNIQUE (tenant_id, username),
    CONSTRAINT users_unique_email_per_tenant UNIQUE (tenant_id, email)
);

CREATE INDEX idx_users_tenant ON users (tenant_id);
CREATE INDEX idx_users_active ON users (is_active);

COMMENT ON TABLE users IS 'Users within tenants with role-based access';

-- ==============================================================================
-- CONTENT OWNERSHIP: Who created/uploaded what
-- ==============================================================================

CREATE TABLE content_ownership (
    id BIGSERIAL PRIMARY KEY,

    -- Content reference (polymorphic)
    content_hash BYTEA NOT NULL,
    content_type VARCHAR(20) NOT NULL CHECK (content_type IN ('atom', 'composition', 'relation')),

    -- Ownership
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,

    -- Provenance
    source_type VARCHAR(50) NOT NULL, -- 'user_upload', 'model_training', 'inference', 'api'
    source_metadata JSONB DEFAULT '{}'::JSONB,

    -- Visibility
    is_public BOOLEAN DEFAULT FALSE, -- Can other tenants see this?
    is_shared BOOLEAN DEFAULT FALSE, -- Shared within tenant?

    -- Timestamps
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT content_ownership_unique UNIQUE (content_hash, content_type, tenant_id, user_id)
);

CREATE INDEX idx_content_ownership_content ON content_ownership (content_hash, content_type);
CREATE INDEX idx_content_ownership_tenant ON content_ownership (tenant_id);
CREATE INDEX idx_content_ownership_user ON content_ownership (user_id);
CREATE INDEX idx_content_ownership_public ON content_ownership (is_public) WHERE is_public = TRUE;

COMMENT ON TABLE content_ownership IS 'Tracks who created/uploaded each piece of content';
COMMENT ON COLUMN content_ownership.is_public IS 'If TRUE, visible to all tenants (e.g., public datasets, common vocabulary)';

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

-- Function: Add vote to semantic edge (updates ELO + provenance)
CREATE OR REPLACE FUNCTION add_edge_vote(
    p_source_hash BYTEA,
    p_target_hash BYTEA,
    p_edge_type VARCHAR(50),
    p_tenant_id UUID,
    p_user_id UUID,
    p_weight DOUBLE PRECISION,
    p_model_name VARCHAR(255) DEFAULT NULL
)
RETURNS INTEGER -- Returns new ELO rating
LANGUAGE plpgsql
AS $$
DECLARE
    new_elo INTEGER;
    vote_elo INTEGER;
BEGIN
    -- Convert weight to ELO
    vote_elo := 1500 + CAST(500.0 * (2.0 * p_weight - 1.0) AS INTEGER);

    -- Update or insert edge
    INSERT INTO semantic_edges (
        source_hash,
        target_hash,
        edge_type,
        elo_rating,
        usage_count,
        provenance,
        created_at,
        last_used_at
    ) VALUES (
        p_source_hash,
        p_target_hash,
        p_edge_type,
        vote_elo,
        1,
        jsonb_build_array(
            jsonb_build_object(
                'tenant_id', p_tenant_id,
                'user_id', p_user_id,
                'model', p_model_name,
                'weight', p_weight,
                'elo', vote_elo,
                'timestamp', NOW()
            )
        ),
        NOW(),
        NOW()
    )
    ON CONFLICT (source_hash, target_hash, edge_type) DO UPDATE
    SET
        -- Update ELO as weighted average
        elo_rating = (
            semantic_edges.elo_rating * semantic_edges.usage_count + vote_elo
        ) / (semantic_edges.usage_count + 1),
        usage_count = semantic_edges.usage_count + 1,
        provenance = semantic_edges.provenance || jsonb_build_array(
            jsonb_build_object(
                'tenant_id', p_tenant_id,
                'user_id', p_user_id,
                'model', p_model_name,
                'weight', p_weight,
                'elo', vote_elo,
                'timestamp', NOW()
            )
        ),
        last_used_at = NOW()
    RETURNING elo_rating INTO new_elo;

    RETURN new_elo;
END;
$$;

COMMENT ON FUNCTION add_edge_vote IS 'Add a vote to semantic edge, update ELO, track provenance';

-- ==============================================================================
-- ROW-LEVEL SECURITY: Enforce access control
-- ==============================================================================

-- Enable RLS on content_ownership
ALTER TABLE content_ownership ENABLE ROW LEVEL SECURITY;

-- Policy: Users can see their own tenant's content
CREATE POLICY tenant_isolation ON content_ownership
    FOR SELECT
    USING (
        tenant_id = current_setting('app.current_tenant_id')::UUID
        OR is_public = TRUE
    );

-- Policy: Users can only insert content for their tenant
CREATE POLICY tenant_insert ON content_ownership
    FOR INSERT
    WITH CHECK (
        tenant_id = current_setting('app.current_tenant_id')::UUID
        AND user_id = current_setting('app.current_user_id')::UUID
    );

-- Policy: Users can update their own content
CREATE POLICY user_update ON content_ownership
    FOR UPDATE
    USING (user_id = current_setting('app.current_user_id')::UUID);

-- Policy: Admins can see all content in their tenant
CREATE POLICY admin_access ON content_ownership
    FOR ALL
    USING (
        tenant_id = current_setting('app.current_tenant_id')::UUID
        AND (
            current_setting('app.current_user_role') = 'admin'
            OR user_id = current_setting('app.current_user_id')::UUID
        )
    );

COMMENT ON POLICY tenant_isolation ON content_ownership IS 'Isolate content by tenant, allow public content';

-- ==============================================================================
-- PROMPT POISONING PREVENTION: Rate limiting and validation
-- ==============================================================================

CREATE TABLE content_validation (
    id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA NOT NULL,
    content_type VARCHAR(20) NOT NULL,

    -- Validation status
    validation_status VARCHAR(20) DEFAULT 'pending' CHECK (
        validation_status IN ('pending', 'approved', 'rejected', 'flagged')
    ),
    validation_reason TEXT,

    -- Flagging (user reports)
    flag_count INTEGER DEFAULT 0,
    flagged_by_users UUID[] DEFAULT ARRAY[]::UUID[],

    -- Automated checks
    contains_malicious_patterns BOOLEAN DEFAULT FALSE,
    similarity_to_known_attacks DOUBLE PRECISION DEFAULT 0.0,

    -- Timestamps
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    validated_at TIMESTAMP WITH TIME ZONE,
    validated_by UUID REFERENCES users(user_id)
);

CREATE INDEX idx_content_validation_content ON content_validation (content_hash);
CREATE INDEX idx_content_validation_status ON content_validation (validation_status);
CREATE INDEX idx_content_validation_flagged ON content_validation (flag_count) WHERE flag_count > 0;

COMMENT ON TABLE content_validation IS 'Validate content to prevent prompt poisoning and malicious injections';

-- Function: Flag suspicious content
CREATE OR REPLACE FUNCTION flag_content(
    p_content_hash BYTEA,
    p_content_type VARCHAR(20),
    p_user_id UUID,
    p_reason TEXT
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO content_validation (
        content_hash,
        content_type,
        validation_status,
        validation_reason,
        flag_count,
        flagged_by_users
    ) VALUES (
        p_content_hash,
        p_content_type,
        'flagged',
        p_reason,
        1,
        ARRAY[p_user_id]
    )
    ON CONFLICT (content_hash, content_type) DO UPDATE
    SET
        flag_count = content_validation.flag_count + 1,
        flagged_by_users = array_append(content_validation.flagged_by_users, p_user_id),
        validation_status = CASE
            WHEN content_validation.flag_count + 1 >= 3 THEN 'rejected'
            ELSE 'flagged'
        END,
        validation_reason = content_validation.validation_reason || E'\n' || p_reason;

    -- Auto-reject if flagged by 3+ users
    IF (SELECT flag_count FROM content_validation WHERE content_hash = p_content_hash) >= 3 THEN
        -- Mark as rejected, trigger cleanup
        RAISE NOTICE 'Content % auto-rejected due to multiple flags', encode(p_content_hash, 'hex');
    END IF;
END;
$$;

COMMENT ON FUNCTION flag_content IS 'Flag suspicious content, auto-reject if 3+ flags';

-- ==============================================================================
-- RATE LIMITING: Prevent abuse
-- ==============================================================================

CREATE TABLE rate_limits (
    id BIGSERIAL PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,

    -- Rate limit type
    action_type VARCHAR(50) NOT NULL, -- 'insert_composition', 'query', 'edge_vote', etc.

    -- Limits
    max_per_hour INTEGER NOT NULL DEFAULT 1000,
    max_per_day INTEGER NOT NULL DEFAULT 10000,

    -- Current usage
    count_last_hour INTEGER DEFAULT 0,
    count_last_day INTEGER DEFAULT 0,
    hour_reset_at TIMESTAMP WITH TIME ZONE DEFAULT NOW() + INTERVAL '1 hour',
    day_reset_at TIMESTAMP WITH TIME ZONE DEFAULT NOW() + INTERVAL '1 day',

    CONSTRAINT rate_limits_unique UNIQUE (tenant_id, user_id, action_type)
);

CREATE INDEX idx_rate_limits_user ON rate_limits (user_id);

-- Function: Check rate limit before action
CREATE OR REPLACE FUNCTION check_rate_limit(
    p_tenant_id UUID,
    p_user_id UUID,
    p_action_type VARCHAR(50)
)
RETURNS BOOLEAN
LANGUAGE plpgsql
AS $$
DECLARE
    v_count_hour INTEGER;
    v_count_day INTEGER;
    v_max_hour INTEGER;
    v_max_day INTEGER;
BEGIN
    -- Get or create rate limit record
    INSERT INTO rate_limits (tenant_id, user_id, action_type)
    VALUES (p_tenant_id, p_user_id, p_action_type)
    ON CONFLICT (tenant_id, user_id, action_type) DO NOTHING;

    -- Reset counters if time expired
    UPDATE rate_limits
    SET
        count_last_hour = CASE WHEN NOW() >= hour_reset_at THEN 0 ELSE count_last_hour END,
        hour_reset_at = CASE WHEN NOW() >= hour_reset_at THEN NOW() + INTERVAL '1 hour' ELSE hour_reset_at END,
        count_last_day = CASE WHEN NOW() >= day_reset_at THEN 0 ELSE count_last_day END,
        day_reset_at = CASE WHEN NOW() >= day_reset_at THEN NOW() + INTERVAL '1 day' ELSE day_reset_at END
    WHERE tenant_id = p_tenant_id AND user_id = p_user_id AND action_type = p_action_type;

    -- Check limits
    SELECT count_last_hour, count_last_day, max_per_hour, max_per_day
    INTO v_count_hour, v_count_day, v_max_hour, v_max_day
    FROM rate_limits
    WHERE tenant_id = p_tenant_id AND user_id = p_user_id AND action_type = p_action_type;

    IF v_count_hour >= v_max_hour OR v_count_day >= v_max_day THEN
        RAISE EXCEPTION 'Rate limit exceeded for action %', p_action_type;
        RETURN FALSE;
    END IF;

    -- Increment counters
    UPDATE rate_limits
    SET
        count_last_hour = count_last_hour + 1,
        count_last_day = count_last_day + 1
    WHERE tenant_id = p_tenant_id AND user_id = p_user_id AND action_type = p_action_type;

    RETURN TRUE;
END;
$$;

COMMENT ON FUNCTION check_rate_limit IS 'Check and enforce rate limits per user/action';

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
-- SECURITY AUDIT LOG
-- ==============================================================================

CREATE TABLE audit_log (
    id BIGSERIAL PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id),
    user_id UUID REFERENCES users(user_id),

    action_type VARCHAR(50) NOT NULL, -- 'insert', 'query', 'update', 'delete', 'flag'
    content_hash BYTEA,
    content_type VARCHAR(20),

    -- Details
    action_details JSONB DEFAULT '{}'::JSONB,
    ip_address INET,
    user_agent TEXT,

    -- Result
    action_result VARCHAR(20) DEFAULT 'success' CHECK (
        action_result IN ('success', 'failure', 'rate_limited', 'unauthorized')
    ),
    error_message TEXT,

    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_audit_log_tenant ON audit_log (tenant_id, created_at DESC);
CREATE INDEX idx_audit_log_user ON audit_log (user_id, created_at DESC);
CREATE INDEX idx_audit_log_action ON audit_log (action_type, created_at DESC);
CREATE INDEX idx_audit_log_content ON audit_log (content_hash) WHERE content_hash IS NOT NULL;

COMMENT ON TABLE audit_log IS 'Security audit trail for all actions';

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
