-- ============================================================================
-- Spatial Centroid Aggregate
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Weighted centroid for semantic clustering
-- ============================================================================

CREATE AGGREGATE spatial_centroid_weighted(GEOMETRY, BIGINT) (
    SFUNC = ST_Collect,
    STYPE = GEOMETRY,
    FINALFUNC = ST_Centroid
);

COMMENT ON AGGREGATE spatial_centroid_weighted(GEOMETRY, BIGINT) IS 
'Compute weighted centroid of geometry collection (weight = reference_count).';


-- Usage example:
-- SELECT spatial_centroid_weighted(spatial_key, reference_count)
-- FROM atom
-- WHERE metadata->>'modality' = 'concept';
