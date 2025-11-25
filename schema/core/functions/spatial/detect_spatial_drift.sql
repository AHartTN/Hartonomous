-- ============================================================================
-- Detect Spatial Drift
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Identify atoms whose positions have become inaccurate
-- ============================================================================

CREATE OR REPLACE FUNCTION detect_spatial_drift()
RETURNS TABLE(atom_id BIGINT, canonical_text TEXT, drift_magnitude REAL)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH position_comparison AS (
        SELECT 
            a.atom_id,
            a.canonical_text,
            a.spatial_key as current_position,
            compute_spatial_position(a.atom_id) as ideal_position
        FROM atom a
        WHERE a.spatial_key IS NOT NULL
          AND a.reference_count > 100  -- Only check frequently-used atoms
        LIMIT 1000  -- Sample size
    )
    SELECT 
        pc.atom_id,
        pc.canonical_text,
        ST_Distance(pc.current_position, pc.ideal_position) as drift
    FROM position_comparison pc
    WHERE ST_Distance(pc.current_position, pc.ideal_position) > 0.5  -- Drift threshold
    ORDER BY drift DESC;
END;
$$;

COMMENT ON FUNCTION detect_spatial_drift() IS 
'Detect atoms whose spatial positions have drifted from their ideal locations.';
