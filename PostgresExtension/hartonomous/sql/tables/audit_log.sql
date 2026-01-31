CREATE TABLE IF NOT EXISTS hartonomous.AuditLog (
    Id BIGSERIAL PRIMARY KEY,
    TenantId UUID NOT NULL REFERENCES hartonomous.Tenant(Id),
    UserId UUID REFERENCES hartonomous."User"(Id),
    ActionType VARCHAR(50) NOT NULL,
    ContentHash BYTEA,
    ContentType VARCHAR(20),
    ActionDetails JSONB DEFAULT '{}'::JSONB,
    IPAddress INET,
    UserAgent TEXT,
    ActionResult VARCHAR(20) DEFAULT 'success',
    ErrorMessage TEXT,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_AuditLog_Tenant ON hartonomous.AuditLog (TenantId, CreatedAt DESC);
CREATE INDEX idx_AuditLog_User ON hartonomous.AuditLog (UserId, CreatedAt DESC);