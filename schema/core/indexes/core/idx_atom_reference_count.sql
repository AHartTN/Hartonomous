-- ============================================================================
-- Atom Reference Count Index (Atomic Mass)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_atom_reference_count 
    ON atom (reference_count DESC);

COMMENT ON INDEX idx_atom_reference_count IS 
'Fast filtering of heavy/important atoms by reference count.';
