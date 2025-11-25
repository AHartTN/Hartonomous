-- ============================================================================
-- Semantic Clusters View (Materialized)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Pre-computed semantic clusters for AI reasoning
-- ============================================================================

CREATE MATERIALIZED VIEW IF NOT EXISTS v_semantic_clusters AS
WITH grid_clusters AS (
    SELECT 
        ST_SnapToGrid(spatial_key, 0.5) AS cluster_center,
        COUNT(*) AS cluster_size,
        AVG(reference_count) AS avg_importance,
        COUNT(DISTINCT metadata->>'modality') AS modality_diversity,
        ARRAY_AGG(DISTINCT metadata->>'model_name') FILTER (WHERE metadata->>'model_name' IS NOT NULL) AS source_models,
        STRING_AGG(DISTINCT canonical_text, ', ' ORDER BY reference_count DESC) AS top_concepts,
        ST_Centroid(ST_Collect(spatial_key)) AS true_centroid
    FROM atom
    WHERE spatial_key IS NOT NULL
    GROUP BY ST_SnapToGrid(spatial_key, 0.5)
)
SELECT 
    cluster_center,
    cluster_size,
    avg_importance,
    modality_diversity,
    source_models,
    ARRAY_LENGTH(source_models, 1) AS model_consensus,
    LEFT(top_concepts, 200) AS sample_concepts,
    true_centroid,
    -- Truth convergence indicator
    CASE 
        WHEN ARRAY_LENGTH(source_models, 1) >= 3 AND cluster_size > 10 
        THEN 'high_consensus'
        WHEN cluster_size > 5 
        THEN 'moderate_consensus'
        ELSE 'low_consensus'
    END AS truth_confidence
FROM grid_clusters
WHERE cluster_size > 1
ORDER BY cluster_size DESC;

CREATE UNIQUE INDEX ON v_semantic_clusters (cluster_center);
CREATE INDEX ON v_semantic_clusters USING GIST (true_centroid);
CREATE INDEX ON v_semantic_clusters (truth_confidence);

COMMENT ON MATERIALIZED VIEW v_semantic_clusters IS 
'Pre-computed semantic clusters for fast AI reasoning. 
Includes truth convergence analysis (multi-model consensus).
Refresh: REFRESH MATERIALIZED VIEW CONCURRENTLY v_semantic_clusters;';
