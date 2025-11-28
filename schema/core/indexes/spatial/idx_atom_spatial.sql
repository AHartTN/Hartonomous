-- ============================================================================
-- Atom Spatial Indexes (Dual Strategy)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Dual indexing strategy for exact and approximate NN queries
-- - GiST on (X,Y,Z): Exact spatial queries via R-tree
-- - B-tree on M: Fast approximate NN via Hilbert curve locality
-- Performance: O(log N) for both strategies
-- ============================================================================

-- Primary spatial index for exact KNN queries (R-tree via GIST)
CREATE INDEX IF NOT EXISTS idx_atom_spatial_xyz 
    ON atom USING GIST (spatial_key)
    WHERE spatial_key IS NOT NULL;

-- Hilbert index for approximate KNN queries (B-tree)
CREATE INDEX IF NOT EXISTS idx_atom_hilbert 
    ON atom ((ST_M(spatial_key)))
    WHERE spatial_key IS NOT NULL;

COMMENT ON INDEX idx_atom_spatial_xyz IS 
'R-tree spatial index on (X,Y,Z) for exact O(log N) KNN queries. Position = semantic meaning.
Exact KNN query: SELECT * FROM atom ORDER BY spatial_key <-> ST_MakePoint(x,y,z) LIMIT k;';

COMMENT ON INDEX idx_atom_hilbert IS 
'B-tree index on M coordinate (Hilbert curve index) for approximate NN queries.
Hilbert curve preserves locality: nearby points in 3D space → nearby values in M.
Approximate KNN: SELECT * FROM atom WHERE ST_M(spatial_key) BETWEEN h-δ AND h+δ ORDER BY spatial_key <-> point LIMIT k;';
