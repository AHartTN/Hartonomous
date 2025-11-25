-- ============================================================================
-- Atom Temporal Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_atom_temporal 
    ON atom (valid_from, valid_to);

COMMENT ON INDEX idx_atom_temporal IS 
'Time-travel queries and temporal versioning.';
