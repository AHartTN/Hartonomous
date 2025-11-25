-- ============================================================================
-- AtomComposition Parent Index (Hierarchy Traversal)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_composition_parent 
    ON atom_composition(parent_atom_id, sequence_index);

COMMENT ON INDEX idx_composition_parent IS 
'Decompose: parent ? components (with sequence order).';
