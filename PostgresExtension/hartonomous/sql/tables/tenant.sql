CREATE TABLE IF NOT EXISTS hartonomous.Tenant (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    Name VARCHAR(255) NOT NULL UNIQUE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    IsActive BOOLEAN DEFAULT TRUE,
    MaxStorageGB INTEGER DEFAULT 100,
    MaxCompositions INTEGER DEFAULT 10000000,
    MaxRelations INTEGER DEFAULT 1000000,
    SubscriptionTier VARCHAR(50) DEFAULT 'free',
    BillingEmail VARCHAR(255)
);

CREATE INDEX IF NOT EXISTS idx_tenants_active ON hartonomous.Tenant(IsActive);
