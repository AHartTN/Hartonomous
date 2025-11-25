-- ============================================================================
-- AtomRelation Temporal Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_relation_temporal 
    ON atom_relation(valid_from, valid_to);

COMMENT ON INDEX idx_relation_temporal IS 
'Time-travel queries for relation history.';
