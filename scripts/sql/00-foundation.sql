-- ==============================================================================
-- Hartonomous Foundation Schema
-- ==============================================================================

-- Enable Row-Level Security (RLS)
ALTER DATABASE hartonomous SET row_security = on;

-- Enable PostGIS for spatial queries
CREATE EXTENSION IF NOT EXISTS postgis;

-- PostGIS topology support
CREATE EXTENSION IF NOT EXISTS postgis_topology;

-- PostGIS indexing support
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- Enable pgcrypto for hashing functions
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Enable Hartonomous extension (BLAKE3, S³ projection, etc.)
CREATE EXTENSION IF NOT EXISTS hartonomous;

-- Create schemas for organization
CREATE SCHEMA IF NOT EXISTS hartonomous;
CREATE SCHEMA IF NOT EXISTS hartonomous_internal;

-- Set search path
SET search_path TO hartonomous, public;

\i domains/uint16.sql
\i domains/uint32.sql
\i domains/uint64.sql
\i domains/uint128.sql

\i types/ingestion_stats.sql
\i types/query_results.sql

\i tables/hartonomous_internal/schema_version.sql

-- Record this schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (1, 'Foundation schema with PostGIS and custom types')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;

-- Success message
DO $$
BEGIN
    RAISE NOTICE 'Foundation schema installed successfully';
END $$;
