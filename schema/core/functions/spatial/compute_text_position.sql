-- ============================================================================
-- Text-Based Spatial Positioning
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Compute position using Levenshtein distance for text similarity
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_text_position(
    p_atom_id BIGINT,
    p_canonical_text TEXT,
    p_neighbor_count INTEGER
)
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_centroid GEOMETRY;
BEGIN
    -- Find similar text atoms with spatial positions
    SELECT ST_Centroid(ST_Collect(a.spatial_key))
    INTO v_centroid
    FROM atom a
    WHERE a.metadata->>'modality' IN ('text', 'character', 'word', 'sentence', 'concept')
      AND a.spatial_key IS NOT NULL
      AND a.atom_id != p_atom_id
      AND a.canonical_text IS NOT NULL
      AND p_canonical_text IS NOT NULL
    ORDER BY levenshtein(
        LEFT(a.canonical_text, 255), 
        LEFT(p_canonical_text, 255)
    ) ASC
    LIMIT p_neighbor_count;
    
    RETURN v_centroid;
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION compute_text_position(BIGINT, TEXT, INTEGER) IS 
'Compute spatial position for text atoms using Levenshtein distance.
Finds K nearest text atoms and returns centroid of their positions.';
