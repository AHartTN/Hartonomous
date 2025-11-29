-- ============================================================================
-- Citus Extensions - Must run BEFORE table creation
-- ============================================================================

-- Citus: Distributed PostgreSQL extension
CREATE EXTENSION IF NOT EXISTS citus;

-- Citus Columnar: Columnar storage for PostgreSQL tables
-- CRITICAL: Must be created before any tables use "USING columnar"
CREATE EXTENSION IF NOT EXISTS citus_columnar;

COMMENT ON EXTENSION citus IS 
'Citus distributed PostgreSQL';

COMMENT ON EXTENSION citus_columnar IS 
'Columnar storage with ORC-inspired format, zstd compression, and skip indexes';
