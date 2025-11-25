-- ============================================================================
-- Indexing Extensions Configuration
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- btree_gin: GIN indexes for B-tree data types
-- Enables composite indexes on JSONB and other types
CREATE EXTENSION IF NOT EXISTS btree_gin;

COMMENT ON EXTENSION btree_gin IS 
'Support for indexing common datatypes in GIN indexes';
