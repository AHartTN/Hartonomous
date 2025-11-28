-- ============================================================================
-- Spatial Centroid Aggregate
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Weighted centroid for semantic clustering
-- Note: This is simplified to unweighted centroid since PostGIS ST_Collect
-- doesn't support weighted aggregation. For true weighted centroids, use
-- manual calculation with ST_X/Y/Z and SUM(value * weight)/SUM(weight).
-- ============================================================================

CREATE AGGREGATE spatial_centroid_weighted(GEOMETRY) (
    SFUNC = ST_Collect,
    STYPE = GEOMETRY,
    FINALFUNC = ST_Centroid
);

COMMENT ON AGGREGATE spatial_centroid_weighted(GEOMETRY) IS 
'Compute centroid of geometry collection. Note: Currently unweighted - for weighted centroids, manually calculate using coordinate * weight aggregation.';


-- Usage example:
-- SELECT spatial_centroid_weighted(spatial_key)
-- FROM atom
-- WHERE metadata->>'modality' = 'concept';
