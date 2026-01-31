CREATE TABLE IF NOT EXISTS hartonomous."User" (
    Id UUID PRIMARY KEY,
    TenantId UUID NOT NULL REFERENCES hartonomous.Tenant(Id) ON DELETE CASCADE,
    Username VARCHAR(50) NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    Role VARCHAR(20) NOT NULL DEFAULT 'user',
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    LastLoginAt TIMESTAMP WITH TIME ZONE,
    UNIQUE(TenantId, Username)
);

CREATE INDEX IF NOT EXISTS idx_users_tenant ON hartonomous."User"(TenantId);
CREATE INDEX IF NOT EXISTS idx_users_active ON hartonomous."User"(IsActive);
