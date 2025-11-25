-- ============================================================================
-- Numeric Spatial Positioning
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Compute position using numeric value proximity
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_numeric_position(
    p_atom_id BIGINT,
    p_value_text TEXT,
    p_neighbor_count INTEGER
)
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_centroid GEOMETRY;
    v_value NUMERIC;
BEGIN
    -- Parse numeric value
    BEGIN
        v_value := p_value_text::NUMERIC;
    EXCEPTION
        WHEN OTHERS THEN
            RETURN NULL;
    END;
    
    -- Find numerically similar atoms
    SELECT ST_Centroid(ST_Collect(a.spatial_key))
    INTO v_centroid
    FROM atom a
    WHERE a.metadata->>'modality' = 'numeric'
      AND a.spatial_key IS NOT NULL
      AND a.atom_id != p_atom_id
      AND a.canonical_text IS NOT NULL
    ORDER BY ABS((a.canonical_text::NUMERIC) - v_value) ASC
    LIMIT p_neighbor_count;
    
    RETURN v_centroid;
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION compute_numeric_position(BIGINT, TEXT, INTEGER) IS 
'Compute spatial position for numeric atoms using value proximity.';
