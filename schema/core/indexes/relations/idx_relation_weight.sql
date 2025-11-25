-- ============================================================================
-- AtomRelation Weight Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_relation_weight 
    ON atom_relation(weight DESC);

COMMENT ON INDEX idx_relation_weight IS 
'Find strongest synaptic connections.';
