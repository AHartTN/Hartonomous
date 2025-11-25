-- ============================================================================
-- Numeric Atomization Function
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Atomize numeric values with type preservation
-- 
-- Handles integers, floats, and numeric types. Converts to binary
-- representation for content-addressable storage.
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_numeric(
    p_value NUMERIC,
    p_metadata JSONB DEFAULT '{}'::jsonb
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
        'modality', 'numeric',
        'original_value', p_value::TEXT,
        'data_type', pg_typeof(p_value)::TEXT
    );
    
    -- Atomize using core function
    RETURN atomize_value(
        v_bytes,
        p_value::TEXT,  -- Canonical text representation
        v_numeric_metadata
    );
    
EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'Numeric atomization failed: %', SQLERRM;
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION atomize_numeric(NUMERIC, JSONB) IS 
'Atomize numeric values (integers, floats, decimals) with type preservation.

Parameters:
  p_value - Numeric value to atomize
  p_metadata - Optional metadata

Returns:
  BIGINT - atom_id

Example:
  SELECT atomize_numeric(3.14159);
  SELECT atomize_numeric(42, ''{"source": "calculation"}''::jsonb);';
