-- ============================================================================
-- Numeric Atomization Function with Spatial Positioning
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Atomize numeric values with spatial positioning for geometric queries
-- 
-- Extends atomize_numeric() to support spatial_key geometry for:
--   - K-nearest neighbor queries
--   - Voronoi cell detection
--   - Hilbert curve traversal
--
-- Used for model weight atomization where:
--   X = layer index
--   Y = head/channel index  
--   Z = normalized weight value
--   M = Hilbert index for cache-coherent traversal
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_numeric_spatial(
    p_value NUMERIC,
    p_metadata JSONB DEFAULT '{}'::jsonb,
    p_spatial_key GEOMETRY(PointZM) DEFAULT NULL
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_bytes BYTEA;
    v_numeric_metadata JSONB;
BEGIN
    -- Convert numeric to text then to binary
    -- This preserves precision while enabling content addressing
    v_bytes := convert_to(p_value::TEXT, 'UTF8');
    
    -- Add numeric type metadata
    v_numeric_metadata := p_metadata || jsonb_build_object(
        'modality', COALESCE(p_metadata->>'modality', 'numeric'),
        'original_value', p_value::TEXT,
        'data_type', pg_typeof(p_value)::TEXT
    );
    
    -- Atomize using spatial function with geometry
    RETURN atomize_value_spatial(
        v_bytes,
        p_value::TEXT,  -- Canonical text representation
        v_numeric_metadata,
        p_spatial_key   -- Pass spatial coordinates
    );
    
EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'Numeric spatial atomization failed: %', SQLERRM;
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION atomize_numeric_spatial(NUMERIC, JSONB, GEOMETRY) IS 
'Atomize numeric values with spatial positioning for geometric queries.

Used for model weights where position encodes semantic structure:
  X = layer index (depth in network)
  Y = head/channel index (parallel processing unit)
  Z = normalized weight value (magnitude)
  M = Hilbert index (cache-coherent traversal)

Parameters:
  p_value - Numeric value to atomize
  p_metadata - Metadata with modality, layer_id, head_id, etc.
  p_spatial_key - PointZM geometry for spatial queries

Returns:
  BIGINT - atom_id

Example:
  SELECT atomize_numeric_spatial(
    3.14159,
    ''{"modality": "weight", "layer": 5, "head": 2}''::jsonb,
    ST_MakePoint(5.0, 2.0, 0.314159, 12345)
  );';
