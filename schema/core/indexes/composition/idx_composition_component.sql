-- ============================================================================
-- AtomComposition Component Index (Inverse Lookup)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_composition_component 
    ON atom_composition(component_atom_id);

COMMENT ON INDEX idx_composition_component IS 
'Inverse: find all parents of a component.';
