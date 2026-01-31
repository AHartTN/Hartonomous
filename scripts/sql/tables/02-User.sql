-- ==============================================================================
-- User: Users within tenants with role-based access
-- ==============================================================================

CREATE TABLE IF NOT EXISTS "User" (
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

CREATE INDEX IF NOT EXISTS idx_users_tenant ON "User"(TenantId);
CREATE INDEX IF NOT EXISTS idx_users_active ON "User"(IsActive);

COMMENT ON TABLE "User" IS 'Users within tenants with role-based access';
COMMENT ON COLUMN "User".Id IS 'Unique identifier for the user';
COMMENT ON COLUMN "User".TenantId IS 'Reference to the tenant organization';
COMMENT ON COLUMN "User".Username IS 'Username for login (unique per tenant)';
COMMENT ON COLUMN "User".PasswordHash IS 'Bcrypt/Argon2 hash of the user password';
COMMENT ON COLUMN "User".Role IS 'Role-based access control (admin, user, readonly)';
COMMENT ON COLUMN "User".IsActive IS 'Whether the user account is active';
COMMENT ON COLUMN "User".CreatedAt IS 'Timestamp of user creation';
COMMENT ON COLUMN "User".LastLoginAt IS 'Timestamp of last successful login';