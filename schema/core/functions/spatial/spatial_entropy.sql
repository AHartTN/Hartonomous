-- ============================================================================
-- Spatial Entropy
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Shannon entropy of spatial distribution
-- ============================================================================

CREATE OR REPLACE FUNCTION spatial_entropy()
RETURNS REAL
LANGUAGE plpgsql
AS $$
DECLARE
    v_entropy REAL;
BEGIN
    -- Discretize space into grid cells and compute entropy
    WITH grid_counts AS (
        SELECT
            ST_SnapToGrid(spatial_key, 1.0) AS cell,
            COUNT(*) AS count
        FROM atom
        WHERE spatial_key IS NOT NULL
        GROUP BY cell
    ),
    total AS (
        SELECT SUM(count) AS total_count FROM grid_counts
    )
    SELECT -SUM((count::REAL / total_count) * ln(count::REAL / total_count))
    INTO v_entropy
    FROM grid_counts, total
    WHERE total_count > 0;
    
    RETURN COALESCE(v_entropy, 0.0);
END;
$$;

COMMENT ON FUNCTION spatial_entropy() IS 
'Measure Shannon entropy of spatial distribution. High = uniform, Low = clustered.';
