-- ============================================================================
-- Citus Extension Configuration
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- Citus: Distributed PostgreSQL extension
CREATE EXTENSION IF NOT EXISTS citus;

-- Citus Columnar: Columnar storage for PostgreSQL tables
-- Provides columnar table access method (2-10x compression, skip indexes)
-- Successor to cstore_fdw with native PostgreSQL integration
CREATE EXTENSION IF NOT EXISTS citus_columnar;

COMMENT ON EXTENSION citus IS 
'Citus distributed PostgreSQL';

COMMENT ON EXTENSION citus_columnar IS 
'Columnar storage with ORC-inspired format, zstd compression, and skip indexes';
