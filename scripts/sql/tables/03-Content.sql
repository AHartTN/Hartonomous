-- ==============================================================================
-- Content: Content records representing ingested digital content
-- ==============================================================================

CREATE TABLE IF NOT EXISTS Content (
    -- Unique BLAKE3 identifier, stored as a UUID (16 bytes)
    Id UUID PRIMARY KEY,

    -- Tenant/User ID for multi-tenancy support and poisoning prevention
    TenantId UUID NOT NULL,
    UserId UUID NOT NULL,
    
    -- The type of the content as an enumeration
    ContentType UINT16 NOT NULL,

    -- Content metadata
    ContentHash BYTEA NOT NULL UNIQUE,
    ContentSize UINT64 NOT NULL,
    ContentMimeType VARCHAR(100),
    ContentLanguage VARCHAR(20),
    ContentSource VARCHAR(255),
    ContentEncoding VARCHAR(50),

    -- Metadata
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Content_ContentType ON Content(ContentType);
CREATE INDEX IF NOT EXISTS idx_Content_TenantId ON Content(TenantId, UserId);
CREATE INDEX IF NOT EXISTS idx_Content_UserId ON Content(UserId);
CREATE INDEX IF NOT EXISTS idx_Content_ContentHash ON Content(ContentHash);

COMMENT ON TABLE Content IS 'Content records representing ingested digital content';
COMMENT ON COLUMN Content.Id IS 'BLAKE3 hash of content metadata (content-addressable key)';
COMMENT ON COLUMN Content.TenantId IS 'The tenant or user ID for multi-tenancy support and poisoning prevention';
COMMENT ON COLUMN Content.UserId IS 'The user ID associated with the content';
COMMENT ON COLUMN Content.ContentType IS 'The type of the content as an enumeration';
COMMENT ON COLUMN Content.ContentHash IS 'The hash of the actual content data';
COMMENT ON COLUMN Content.ContentSize IS 'The size of the content in bytes';
COMMENT ON COLUMN Content.ContentMimeType IS 'The MIME type of the content';
COMMENT ON COLUMN Content.ContentLanguage IS 'The language of the content, if applicable';
COMMENT ON COLUMN Content.ContentSource IS 'The source or origin of the content';
COMMENT ON COLUMN Content.ContentEncoding IS 'The encoding format of the content, if applicable';
COMMENT ON COLUMN Content.CreatedAt IS 'Timestamp of first insertion into the Content table';
COMMENT ON COLUMN Content.ModifiedAt IS 'Timestamp of last modification to the Content record';
COMMENT ON COLUMN Content.ValidatedAt IS 'Timestamp of last validation of the Content record';