-- ============================================================================
-- Atom Spatial Index (R-tree via GIST)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Primary spatial index for K-nearest neighbor (KNN) queries
-- Performance: O(log N) lookups, ~0.3ms for typical queries
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_atom_spatial 
    ON atom USING GIST (spatial_key)
    WHERE spatial_key IS NOT NULL;

COMMENT ON INDEX idx_atom_spatial IS 
'R-tree spatial index for O(log N) KNN queries. Position = semantic meaning.
KNN query: SELECT * FROM atom ORDER BY spatial_key <-> ST_MakePoint(x,y,z) LIMIT k;';
