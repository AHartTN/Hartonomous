-- ============================================================================
-- AtomRelation Last Accessed Index (Synaptic Decay)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_relation_last_accessed 
    ON atom_relation(last_accessed);

COMMENT ON INDEX idx_relation_last_accessed IS 
'Identify unused synapses for decay/pruning.';
