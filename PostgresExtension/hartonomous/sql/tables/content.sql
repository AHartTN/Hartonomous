CREATE TABLE IF NOT EXISTS hartonomous.Content (
    Id UUID PRIMARY KEY,
    TenantId UUID NOT NULL,
    UserId UUID NOT NULL,
    ContentType UINT16 NOT NULL,
    ContentHash BYTEA NOT NULL UNIQUE,
    ContentSize UINT64 NOT NULL,
    ContentMimeType VARCHAR(100),
    ContentLanguage VARCHAR(20),
    ContentSource VARCHAR(255),
    ContentEncoding VARCHAR(50),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Content_ContentType ON hartonomous.Content(ContentType);
CREATE INDEX IF NOT EXISTS idx_Content_TenantId ON hartonomous.Content(TenantId, UserId);
CREATE INDEX IF NOT EXISTS idx_Content_UserId ON hartonomous.Content(UserId);
CREATE INDEX IF NOT EXISTS idx_Content_ContentHash ON hartonomous.Content(ContentHash);
