-- ============================================================================
-- AtomRelation Target Index (Backward Graph Traversal)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_relation_target 
    ON atom_relation(target_atom_id);

COMMENT ON INDEX idx_relation_target IS 
'Backward graph traversal: target ? sources.';
