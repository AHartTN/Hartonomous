-- ============================================================================
-- AtomRelation Spatial Expression Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_relation_spatial 
    ON atom_relation USING GIST (spatial_expression)
    WHERE spatial_expression IS NOT NULL;

COMMENT ON INDEX idx_relation_spatial IS 
'Spatial index for geometric path traversal through semantic space.';
