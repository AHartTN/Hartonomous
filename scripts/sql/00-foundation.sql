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

-- Enable pg_trgm for fuzzy text search (trigram similarity)
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Enable fuzzystrmatch for phonetic search (soundex, metaphone)
CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;

-- Create internal schema for versioning/metadata
CREATE SCHEMA IF NOT EXISTS hartonomous_internal;

-- Set search path (public for all tables, hartonomous_internal for metadata)
SET search_path TO public, hartonomous_internal;

-- Domains MUST be created BEFORE the hartonomous extension,
-- because the extension SQL references uint64 and uint128 types.
\i domains/uint16.sql
\i domains/uint32.sql
\i domains/uint64.sql
\i domains/uint128.sql

-- Helper function needed by views (loaded early so views in core-tables can use it)
\i functions/uint32_to_int.sql

-- Try to enable Hartonomous extension (BLAKE3, S³ projection, uint64 ops, etc.)
-- This is optional - core functionality works without it
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS hartonomous;
    RAISE NOTICE 'Hartonomous extension loaded successfully';
EXCEPTION WHEN OTHERS THEN
    RAISE WARNING 'Hartonomous extension not available - some advanced functions will be unavailable';
    RAISE WARNING 'Details: %', SQLERRM;
END $$;

-- Try to enable S3 extension (geodesic distance, GIST operator class, etc.)
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS s3;
    RAISE NOTICE 'S3 extension loaded successfully';
EXCEPTION WHEN OTHERS THEN
    RAISE WARNING 'S3 extension not available - geometric search functions will be unavailable';
    RAISE WARNING 'Details: %', SQLERRM;
END $$;

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
