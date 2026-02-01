-- ==============================================================================
-- TenantUser: Users within tenants with role-based access
-- ==============================================================================

CREATE TABLE IF NOT EXISTS TenantUser (
    Id UUID PRIMARY KEY,
    TenantId UUID NOT NULL REFERENCES Tenant(Id) ON DELETE CASCADE,
    Username VARCHAR(50) NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    Role VARCHAR(20) NOT NULL DEFAULT 'user',
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    LastLoginAt TIMESTAMP WITH TIME ZONE,    
    UNIQUE(TenantId, Username)
);

CREATE INDEX IF NOT EXISTS idx_tenantuser_tenant ON TenantUser(TenantId);
CREATE INDEX IF NOT EXISTS idx_tenantuser_active ON TenantUser(IsActive);

COMMENT ON TABLE TenantUser IS 'Users within tenants with role-based access';
COMMENT ON COLUMN TenantUser.Id IS 'Unique identifier for the tenant user';
COMMENT ON COLUMN TenantUser.TenantId IS 'Reference to the tenant organization';
COMMENT ON COLUMN TenantUser.Username IS 'Username for login (unique per tenant)';
COMMENT ON COLUMN TenantUser.PasswordHash IS 'Bcrypt/Argon2 hash of the user password';
COMMENT ON COLUMN TenantUser.Role IS 'Role-based access control (admin, user, readonly)';
COMMENT ON COLUMN TenantUser.IsActive IS 'Whether the tenant user account is active';
COMMENT ON COLUMN TenantUser.CreatedAt IS 'Timestamp of tenant user creation';
COMMENT ON COLUMN TenantUser.LastLoginAt IS 'Timestamp of last successful login';