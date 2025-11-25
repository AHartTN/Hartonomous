-- ============================================================================
-- Atom Content Hash Index (Deduplication)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: O(1) content-addressable lookup for global deduplication
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_atom_hash 
    ON atom (content_hash);

COMMENT ON INDEX idx_atom_hash IS 
'O(1) SHA-256 hash lookup for content-addressable deduplication.';
