-- ============================================================================
-- Helper: Atomize with Spatial Key
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Common pattern: atomize value and immediately set spatial_key
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_with_spatial_key(
    p_value BYTEA,
    p_spatial_key GEOMETRY,
    p_canonical_text TEXT DEFAULT NULL,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_atom_id BIGINT;
BEGIN
    v_atom_id := atomize_value(p_value, p_canonical_text, p_metadata);
    
    UPDATE atom 
    SET spatial_key = p_spatial_key 
    WHERE atom_id = v_atom_id;
    
    RETURN v_atom_id;
END;
$$;

COMMENT ON FUNCTION atomize_with_spatial_key(BYTEA, GEOMETRY, TEXT, JSONB) IS 
'Helper: atomize value and set spatial_key in one operation. Reduces code duplication.';
