-- Version tracking
CREATE TABLE IF NOT EXISTS hartonomous_internal.schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    description TEXT
);