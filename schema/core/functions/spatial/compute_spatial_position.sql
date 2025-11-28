-- ============================================================================
-- Spatial Position Computation - Core Algorithm
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Compute 3D spatial position via weighted neighbor averaging
-- 
-- Spatial position = semantic meaning
-- Position is discovered through proximity to semantically similar atoms
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_spatial_position(
    p_atom_id BIGINT,
    p_neighbor_count INTEGER DEFAULT 100
)
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_atom RECORD;
    v_position GEOMETRY;
    v_modality TEXT;
    v_hilbert_index BIGINT;
BEGIN
    -- Validate inputs
    IF p_neighbor_count < 1 THEN
        RAISE EXCEPTION 'Neighbor count must be at least 1, got %', p_neighbor_count;
    END IF;
    
    -- Get atom details
    SELECT 
        metadata->>'modality' as modality,
        canonical_text,
        atomic_value
    INTO v_atom
    FROM atom 
    WHERE atom_id = p_atom_id;
    
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Atom % not found', p_atom_id;
    END IF;
    
    v_modality := COALESCE(v_atom.modality, 'unknown');
    
    -- Compute position based on modality-specific similarity
    v_position := compute_position_by_modality(
        p_atom_id,
        v_modality,
        v_atom.canonical_text,
        v_atom.atomic_value,
        p_neighbor_count
    );
    
    -- If no neighbors found, initialize in bounded random position
    IF v_position IS NULL THEN
        v_position := initialize_random_position();
    END IF;
    
    -- Compute Hilbert index from (X,Y,Z) coordinates and embed in M
    v_hilbert_index := hilbert_index_3d(
        ST_X(v_position),
        ST_Y(v_position),
        ST_Z(v_position),
        10  -- Default order=10 gives 2^10=1024 resolution per dimension
    );
    
    -- Convert POINTZ to POINTZM with M = Hilbert index
    v_position := ST_MakePoint(
        ST_X(v_position),
        ST_Y(v_position),
        ST_Z(v_position),
        v_hilbert_index::DOUBLE PRECISION
    );
    
    RETURN v_position;
    
EXCEPTION
    WHEN OTHERS THEN
        RAISE EXCEPTION 'Spatial position computation failed for atom %: %', 
            p_atom_id, SQLERRM;
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION compute_spatial_position(BIGINT, INTEGER) IS 
'Compute 4D spatial position (POINTZM) via weighted neighbor averaging + Hilbert encoding.
Position in semantic space represents meaning - close in space = similar in meaning.
M coordinate stores Hilbert curve index for fast approximate NN queries.

Parameters:
  p_atom_id - Atom to position
  p_neighbor_count - Number of neighbors to consider (default: 100)

Returns:
  GEOMETRY(POINTZM) - 4D position: (X,Y,Z) = semantic coordinates, M = Hilbert index

Algorithm:
  1. Identify modality of atom
  2. Find K semantically similar atoms with positions
  3. Compute weighted centroid of neighbor positions → (X,Y,Z)
  4. Compute Hilbert curve index from (X,Y,Z) → M
  5. Return POINTZM(X,Y,Z,M) for dual indexing

Example:
  UPDATE atom SET spatial_key = compute_spatial_position(atom_id) 
  WHERE atom_id = 12345;';
