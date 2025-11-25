-- ============================================================================
-- Spatial Density Heatmap View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Pre-computed density for heatmap visualization
-- ============================================================================

CREATE MATERIALIZED VIEW IF NOT EXISTS v_spatial_density AS
WITH grid AS (
    SELECT 
        x, y, z,
        ST_MakePoint(x, y, z) AS grid_point
    FROM generate_series(-10, 10, 0.5) x
    CROSS JOIN generate_series(-10, 10, 0.5) y
    CROSS JOIN generate_series(-10, 10, 0.5) z
)
SELECT 
    g.x, g.y, g.z,
    g.grid_point,
    COUNT(a.atom_id) AS atom_count,
    SUM(a.reference_count) AS total_importance,
    AVG(a.reference_count) AS avg_importance,
    STRING_AGG(DISTINCT a.metadata->>'modality', ', ') AS modalities
FROM grid g
LEFT JOIN atom a ON ST_DWithin(g.grid_point, a.spatial_key, 0.5)
WHERE a.spatial_key IS NOT NULL OR g.grid_point IS NOT NULL
GROUP BY g.x, g.y, g.z, g.grid_point
HAVING COUNT(a.atom_id) > 0
ORDER BY atom_count DESC;

CREATE INDEX ON v_spatial_density (atom_count DESC);
CREATE INDEX ON v_spatial_density USING GIST (grid_point);

COMMENT ON MATERIALIZED VIEW v_spatial_density IS 
'Spatial density heatmap for visualization. Refresh: REFRESH MATERIALIZED VIEW CONCURRENTLY v_spatial_density;';
