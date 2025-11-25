-- ============================================================================
-- AtomComposition Spatial Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_composition_spatial 
    ON atom_composition USING GIST (spatial_key)
    WHERE spatial_key IS NOT NULL;

COMMENT ON INDEX idx_composition_spatial IS 
'Spatial index for local coordinate frame queries in composition hierarchy.';
