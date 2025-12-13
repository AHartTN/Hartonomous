-- Additional performance indexes for Hartonomous
-- Optimizes common query patterns

-- Composite index for hierarchy traversal queries
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_atoms_z_modality 
ON atom (ST_Z(geom), modality) 
WHERE atom_class = 1;

-- Partial index for high-salience atoms (frequently accessed)
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_atoms_high_salience
ON atom USING gist(geom)
WHERE ST_M(geom) > 5.0;

-- BRIN index for creation timestamp (time-series queries)
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_atoms_created_brin
ON atom USING brin(created_at)
WITH (pages_per_range = 128);

-- Expression index for centroid queries
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_atoms_xy_centroid
ON atom ((ST_X(geom) + ST_Y(geom)) / 2.0);

-- Statistics collection
ALTER TABLE atom SET (autovacuum_vacuum_scale_factor = 0.1);
ALTER TABLE atom SET (autovacuum_analyze_scale_factor = 0.05);

-- Update planner statistics
ANALYZE atom;

COMMENT ON INDEX idx_atoms_z_modality IS 'Optimizes composition hierarchy traversal';
COMMENT ON INDEX idx_atoms_high_salience IS 'Fast access to frequently-used atoms';
COMMENT ON INDEX idx_atoms_created_brin IS 'Block-range index for temporal queries';
COMMENT ON INDEX idx_atoms_xy_centroid IS 'Centroid-based spatial queries';
