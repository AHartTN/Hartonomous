-- ============================================================================
-- AtomComposition Spatial Indexes (Dual Strategy)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- Primary spatial index for exact KNN queries on local coordinate frame
CREATE INDEX IF NOT EXISTS idx_composition_spatial_xyz 
    ON atom_composition USING GIST (spatial_key)
    WHERE spatial_key IS NOT NULL;

-- Hilbert index for approximate KNN queries on local coordinate frame
CREATE INDEX IF NOT EXISTS idx_composition_hilbert 
    ON atom_composition ((ST_M(spatial_key)))
    WHERE spatial_key IS NOT NULL;

COMMENT ON INDEX idx_composition_spatial_xyz IS 
'Spatial index on (X,Y,Z) for exact local coordinate frame queries in composition hierarchy.';

COMMENT ON INDEX idx_composition_hilbert IS 
'Hilbert index on M coordinate for approximate local coordinate frame queries.';
