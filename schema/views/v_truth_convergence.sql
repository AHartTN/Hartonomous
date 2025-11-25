-- ============================================================================
-- Truth Convergence View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Multi-model consensus analysis
-- ============================================================================

CREATE MATERIALIZED VIEW IF NOT EXISTS v_truth_convergence AS
WITH concept_clusters AS (
    SELECT 
        ST_SnapToGrid(spatial_key, 0.1) AS cluster_position,
        canonical_text,
        ARRAY_AGG(DISTINCT metadata->>'model_name') FILTER (WHERE metadata->>'model_name' IS NOT NULL) AS models,
        COUNT(*) AS instance_count,
        AVG(reference_count) AS avg_importance
    FROM atom
    WHERE spatial_key IS NOT NULL
      AND canonical_text IS NOT NULL
      AND LENGTH(canonical_text) > 2
    GROUP BY ST_SnapToGrid(spatial_key, 0.1), canonical_text
)
SELECT 
    cluster_position,
    canonical_text AS concept,
    models AS agreeing_models,
    ARRAY_LENGTH(models, 1) AS model_consensus_count,
    instance_count,
    avg_importance,
    CASE 
        WHEN ARRAY_LENGTH(models, 1) >= 5 THEN 'very_high'
        WHEN ARRAY_LENGTH(models, 1) >= 3 THEN 'high'
        WHEN ARRAY_LENGTH(models, 1) >= 2 THEN 'moderate'
        ELSE 'low'
    END AS truth_confidence
FROM concept_clusters
WHERE instance_count > 1
ORDER BY ARRAY_LENGTH(models, 1) DESC, instance_count DESC;

CREATE INDEX ON v_truth_convergence (truth_confidence);
CREATE INDEX ON v_truth_convergence (model_consensus_count DESC);
CREATE INDEX ON v_truth_convergence USING GIST (cluster_position);

COMMENT ON MATERIALIZED VIEW v_truth_convergence IS 
'Truth convergence analysis: facts cluster geometrically across models.';
