-- ============================================================================
-- Modality-Specific Position Computation
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Compute positions using modality-aware similarity metrics
-- 
-- Different modalities use different similarity functions:
--   - Text: Levenshtein distance
--   - Numeric: Value proximity
--   - Other: Reference count weighting
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_position_by_modality(
    p_atom_id BIGINT,
    p_modality TEXT,
    p_canonical_text TEXT,
    p_atom_value BYTEA,
    p_neighbor_count INTEGER
)
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_centroid GEOMETRY;
BEGIN
    -- Text-based modalities: use Levenshtein distance
    IF p_modality IN ('text', 'character', 'word', 'sentence', 'concept') THEN
        v_centroid := compute_text_position(
            p_atom_id, 
            p_canonical_text, 
            p_neighbor_count
        );
        
    -- Numeric modalities: use value proximity
    ELSIF p_modality = 'numeric' THEN
        v_centroid := compute_numeric_position(
            p_atom_id,
            p_canonical_text,
            p_neighbor_count
        );
        
    -- Default: use reference count weighting
    ELSE
        v_centroid := compute_default_position(
            p_atom_id,
            p_modality,
            p_neighbor_count
        );
    END IF;
    
    RETURN v_centroid;
    
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION compute_position_by_modality(BIGINT, TEXT, TEXT, BYTEA, INTEGER) IS 
'Compute spatial position using modality-specific similarity metrics.
Routes to appropriate similarity function based on modality type.';
