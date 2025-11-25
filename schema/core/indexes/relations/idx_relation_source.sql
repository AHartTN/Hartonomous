-- ============================================================================
-- AtomRelation Source Index (Forward Graph Traversal)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_relation_source 
    ON atom_relation(source_atom_id);

COMMENT ON INDEX idx_relation_source IS 
'Forward graph traversal: source ? targets.';
