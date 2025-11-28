-- ============================================================================
-- Default Spatial Positioning
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Compute position using reference count weighting (default strategy)
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_default_position(
    p_atom_id BIGINT,
    p_modality TEXT,
    p_neighbor_count INTEGER
)
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_centroid GEOMETRY;
BEGIN
    -- Find atoms of same modality, weighted by importance (reference_count)
    SELECT ST_Centroid(ST_Collect(spatial_key))
    INTO v_centroid
    FROM (
        SELECT a.spatial_key
        FROM atom a
        WHERE a.metadata->>'modality' = p_modality
          AND a.spatial_key IS NOT NULL
          AND a.atom_id != p_atom_id
        ORDER BY a.reference_count DESC
        LIMIT p_neighbor_count
    ) subq;
    
    RETURN v_centroid;
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION compute_default_position(BIGINT, TEXT, INTEGER) IS 
'Default spatial positioning using reference count as importance weighting.
Used for modalities without specific similarity metrics.';
