-- ============================================================================
-- Mendeleev Audit - Predict Missing Knowledge Atoms
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Find gaps in semantic space (like Mendeleev predicted missing elements)
-- ============================================================================

CREATE OR REPLACE FUNCTION mendeleev_audit()
RETURNS TABLE(
    predicted_position GEOMETRY,
    nearest_neighbors TEXT[],
    confidence REAL
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH grid AS (
        SELECT ST_MakePoint(x, y, z) AS point
        FROM generate_series(-10, 10, 0.5) x
        CROSS JOIN generate_series(-10, 10, 0.5) y
        CROSS JOIN generate_series(-10, 10, 0.5) z
    )
    SELECT
        g.point AS predicted_position,
        ARRAY(
            SELECT a.canonical_text
            FROM atom a
            WHERE a.spatial_key IS NOT NULL
            ORDER BY ST_Distance(g.point, a.spatial_key)
            LIMIT 5
        ) AS neighbors,
        (1.0 / NULLIF(
            (SELECT MIN(ST_Distance(g.point, a.spatial_key))
             FROM atom a WHERE a.spatial_key IS NOT NULL),
            0
        ))::REAL AS confidence
    FROM grid g
    WHERE (
        SELECT MIN(ST_Distance(g.point, a.spatial_key))
        FROM atom a WHERE a.spatial_key IS NOT NULL
    ) > 2.0  -- Large gap threshold
    ORDER BY confidence DESC NULLS LAST
    LIMIT 100;
END;
$$;

COMMENT ON FUNCTION mendeleev_audit() IS 
'Predict missing knowledge atoms by finding gaps in semantic space.';
