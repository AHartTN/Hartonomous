-- ============================================================================
-- AtomComposition Temporal Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_composition_temporal 
    ON atom_composition(valid_from, valid_to);

COMMENT ON INDEX idx_composition_temporal IS 
'Time-travel queries for composition history.';
