-- ==============================================================================
-- TENANTS: Multi-tenant isolation
-- ==============================================================================

CREATE TABLE Tenant (
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

CREATE INDEX idx_tenants_active ON Tenant (is_active);

COMMENT ON TABLE Tenant IS 'Multi-tenant isolation for shared Hartonomous instance';