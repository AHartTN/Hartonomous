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