-- ==============================================================================
-- SECURITY AUDIT LOG
-- ==============================================================================

CREATE TABLE AuditLog (
    Id BIGSERIAL PRIMARY KEY,
    TenantId UUID NOT NULL REFERENCES Tenant(Id),
    UserId UUID REFERENCES User(Id),
    ActionType VARCHAR(50) NOT NULL, -- 'insert', 'query', 'update', 'delete', 'flag'
    ContentHash BYTEA,
    ContentType VARCHAR(20),

    -- Details
    ActionDetails JSONB DEFAULT '{}'::JSONB,
    IPAddress INET,
    UserAgent TEXT,

    -- Result
    ActionResult VARCHAR(20) DEFAULT 'success' CHECK (
        ActionResult IN ('success', 'failure', 'rate_limited', 'unauthorized')
    ),
    ErrorMessage TEXT,

    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_AuditLog_Tenant ON AuditLog (TenantId, CreatedAt DESC);
CREATE INDEX idx_AuditLog_User ON AuditLog (UserId, CreatedAt DESC);
CREATE INDEX idx_AuditLog_Action ON AuditLog (ActionType, CreatedAt DESC);
CREATE INDEX idx_AuditLog_Content ON AuditLog (ContentHash) WHERE ContentHash IS NOT NULL;
CREATE INDEX idx_AuditLog_CreatedAt ON AuditLog (CreatedAt);
CREATE INDEX idx_AuditLog_ModifiedAt ON AuditLog (ModifiedAt);
CREATE INDEX idx_AuditLog_ValidatedAt ON AuditLog (ValidatedAt);

COMMENT ON TABLE AuditLog IS 'Security audit trail for all actions';
COMMENT ON COLUMN AuditLog.Id IS 'Primary key identifier for the audit log entry';
COMMENT ON COLUMN AuditLog.TenantId IS 'Reference to the tenant associated with the action';
COMMENT ON COLUMN AuditLog.UserId IS 'Reference to the user who performed the action, if applicable';
COMMENT ON COLUMN AuditLog.ActionType IS 'Type of action performed, e.g., insert, query, update, delete, flag';
COMMENT ON COLUMN AuditLog.ContentHash IS 'Hash of the content involved in the action, if applicable';
COMMENT ON COLUMN AuditLog.ContentType IS 'Type of content involved in the action, if applicable';
COMMENT ON COLUMN AuditLog.ActionDetails IS 'JSONB object containing additional details about the action performed';
COMMENT ON COLUMN AuditLog.IPAddress IS 'IP address from which the action was performed';
COMMENT ON COLUMN AuditLog.UserAgent IS 'User agent string of the client performing the action';
COMMENT ON COLUMN AuditLog.ActionResult IS 'Result of the action, indicating success or type of failure';
COMMENT ON COLUMN AuditLog.ErrorMessage IS 'Error message if the action resulted in a failure';
COMMENT ON COLUMN AuditLog.CreatedAt IS 'Timestamp when the audit log entry was created';
COMMENT ON COLUMN AuditLog.ModifiedAt IS 'Timestamp when the audit log entry was last modified';
COMMENT ON COLUMN AuditLog.ValidatedAt IS 'Timestamp when the audit log entry was validated';