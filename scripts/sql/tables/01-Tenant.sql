-- ==============================================================================
-- TENANTS: Multi-tenant isolation
-- ==============================================================================

CREATE TABLE IF NOT EXISTS Tenant (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    Name VARCHAR(255) NOT NULL UNIQUE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    IsActive BOOLEAN DEFAULT TRUE,

    -- Quota limits (prevent abuse)
    MaxStorageGB INTEGER DEFAULT 100,
    MaxCompositions INTEGER DEFAULT 10000000,
    MaxRelations INTEGER DEFAULT 1000000,

    -- Billing
    SubscriptionTier VARCHAR(50) DEFAULT 'free',
    BillingEmail VARCHAR(255)
);

CREATE INDEX IF NOT EXISTS idx_tenants_active ON Tenant(IsActive);

COMMENT ON TABLE Tenant IS 'Multi-tenant isolation for shared Hartonomous instance';
COMMENT ON COLUMN Tenant.Id IS 'Unique identifier for the tenant';