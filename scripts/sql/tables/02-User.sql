-- ==============================================================================
-- USERS: Per-tenant users
-- ==============================================================================

CREATE TABLE User (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId UUID NOT NULL REFERENCES Tenant(Id) ON DELETE CASCADE,
    Username VARCHAR(255) NOT NULL,
    Email VARCHAR(255) NOT NULL,
    Created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE,

    -- Permissions
    role VARCHAR(50) DEFAULT 'user' CHECK (role IN ('admin', 'user', 'readonly')),

    CONSTRAINT users_unique_username_per_tenant UNIQUE (tenant_id, username),
    CONSTRAINT users_unique_email_per_tenant UNIQUE (tenant_id, email)
);

CREATE INDEX idx_users_tenant ON User (tenant_id);
CREATE INDEX idx_users_active ON User (is_active);

COMMENT ON TABLE User IS 'Users within tenants with role-based access';