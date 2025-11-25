-- ============================================================================
-- AtomRelation Type Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_relation_type 
    ON atom_relation(relation_type_id);

COMMENT ON INDEX idx_relation_type IS 
'Filter relations by type (semantic_similar, causes, etc.)';
