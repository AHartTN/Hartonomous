-- ==============================================================================
-- PROMPT POISONING PREVENTION: Rate limiting and validation
-- ==============================================================================

-- NOTE TO AI AGENTS AND DEVELOPERS:
-- This table is designed to help prevent prompt poisoning and malicious injections
-- by tracking content validation status, user reports, and automated checks.

-- IT IS A BROKEN IMPLEMENTATION IN ITS CURRENT STATE AND NEEDS FURTHER DEVELOPMENT.
-- This needs to be more integrated with the core schema instead of isolated like this
-- Additionally, no lazy arrays for collections... We use referential integrity like proper sql developers

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