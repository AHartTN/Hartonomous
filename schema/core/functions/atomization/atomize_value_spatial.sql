-- ============================================================================
-- Spatial Atomization Function
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Content-addressable storage with geometric spatial positioning
-- 
-- Extends the base atomize_value() function to support spatial_key geometry
-- for geometric queries (Voronoi, k-NN, Hilbert traversal). Coordinates:
--   X, Y, Z: Euclidean coordinates for distance-based queries
--   M: Hilbert curve index for cache-coherent traversal
-- 
-- Modalities and their spatial encodings:
--   - tokenizer/vocabulary: X=position, Y=frequency, Z=semantic, M=hilbert(token_id)
--   - architecture/config: X=log(dim), Y=layers, Z=heads, M=hilbert(config_hash)
--   - tensor/weight: X=layer, Y=head, Z=value, M=hilbert([layer,head,row,col])
--   - tokenizer/merge: X=merge_id, Y=priority, Z=component_avg, M=hilbert(merge_id)
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_value_spatial(
    p_value BYTEA,
    p_canonical_text TEXT DEFAULT NULL,
    p_metadata JSONB DEFAULT '{}'::jsonb,
    p_spatial_key GEOMETRY(PointZM) DEFAULT NULL
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_hash BYTEA;
    v_atom_id BIGINT;
BEGIN
    -- Validate size constraint (≤64 bytes)
    IF length(p_value) > 64 THEN
        RAISE EXCEPTION 
            'Atomic value exceeds 64-byte limit (got % bytes). Values larger than 64 bytes must be decomposed into smaller atoms.',
            length(p_value)
            USING HINT = 'Use atomize_text() for strings or decompose manually';
    END IF;
    
    -- Compute SHA-256 hash for content addressing
    v_hash := digest(p_value, 'sha256');
    
    -- Attempt to find existing atom (deduplication)
    SELECT atom_id INTO v_atom_id
    FROM atom 
    WHERE content_hash = v_hash;
    
    IF FOUND THEN
        -- Atom exists: increment reference count (conservation of reference)
        UPDATE atom 
        SET reference_count = reference_count + 1,
            -- Update metadata if new information provided
            metadata = CASE 
                WHEN p_metadata != '{}'::jsonb 
                THEN atom.metadata || p_metadata 
                ELSE atom.metadata 
            END,
            -- Update spatial_key if provided and not already set
            spatial_key = CASE
                WHEN p_spatial_key IS NOT NULL AND atom.spatial_key IS NULL
                THEN p_spatial_key
                ELSE atom.spatial_key
            END
        WHERE atom_id = v_atom_id;
        
        RETURN v_atom_id;
    END IF;
    
    -- Atom doesn't exist: create new atom with spatial positioning
    INSERT INTO atom (
        content_hash, 
        atom_value, 
        canonical_text, 
        metadata,
        spatial_key,
        reference_count
    )
    VALUES (
        v_hash, 
        p_value, 
        p_canonical_text, 
        p_metadata,
        p_spatial_key,
        1  -- Initial reference count
    )
    RETURNING atom_id INTO v_atom_id;
    
    RETURN v_atom_id;
    
EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'Spatial atomization failed: %', SQLERRM
            USING HINT = 'Check input value, metadata format, and spatial_key geometry';
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION atomize_value_spatial(BYTEA, TEXT, JSONB, GEOMETRY) IS 
'Spatial atomization interface: SHA-256 content addressing with geometric positioning.
Returns atom_id of created or existing atom. Enforces ≤64 byte constraint.

Spatial coordinates (PointZM):
  X, Y, Z: Euclidean coordinates for Voronoi/k-NN distance queries
  M: Hilbert curve index for sequential/cache-coherent traversal

Parameters:
  p_value - Binary value to atomize (≤64 bytes required)
  p_canonical_text - Optional text representation for caching
  p_metadata - Optional JSONB metadata (modality, model_name, token_id, etc.)
  p_spatial_key - Optional PointZM geometry for spatial queries

Returns:
  BIGINT - atom_id of the atomized value

Example:
  SELECT atomize_value_spatial(
    ''\x48''::bytea, 
    ''Hello'', 
    ''{"modality": "tokenizer/vocabulary", "token_id": 42}''::jsonb,
    ST_GeomFromText(''POINT ZM (0.5 0.3 0.8 12345)'')
  );';
