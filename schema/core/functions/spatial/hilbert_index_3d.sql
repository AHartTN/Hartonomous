-- ============================================================================
-- Hilbert Curve Space-Filling for Spatial Indexing Optimization
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Map 3D coordinates to 1D Hilbert curve for better locality
-- Use: Optimize spatial queries and improve cache performance
-- ============================================================================

CREATE OR REPLACE FUNCTION hilbert_index_3d(
    p_x REAL,
    p_y REAL,
    p_z REAL,
    p_order INTEGER DEFAULT 10  -- Hilbert curve order (10 = 1024³ resolution)
)
RETURNS BIGINT
LANGUAGE plpgsql IMMUTABLE
AS $$
DECLARE
    v_max_coord BIGINT;
    v_xi BIGINT;
    v_yi BIGINT;
    v_zi BIGINT;
    v_index BIGINT := 0;
    v_n INTEGER;
    v_rx INTEGER;
    v_ry INTEGER;
    v_rz INTEGER;
    v_s INTEGER;
BEGIN
    -- Normalize coordinates to [0, 2^order)
    v_max_coord := (1 << p_order);  -- 2^order
    
    -- Normalize from [-10, 10] to [0, 2^order)
    v_xi := GREATEST(0, LEAST(v_max_coord - 1, 
        ((p_x + 10.0) / 20.0 * v_max_coord)::BIGINT));
    v_yi := GREATEST(0, LEAST(v_max_coord - 1, 
        ((p_y + 10.0) / 20.0 * v_max_coord)::BIGINT));
    v_zi := GREATEST(0, LEAST(v_max_coord - 1, 
        ((p_z + 10.0) / 20.0 * v_max_coord)::BIGINT));
    
    -- Compute 3D Hilbert index
    v_s := v_max_coord / 2;
    WHILE v_s > 0 LOOP
        v_rx := (v_xi & v_s) >> (ffs(v_s::bit(64)) - 1);
        v_ry := (v_yi & v_s) >> (ffs(v_s::bit(64)) - 1);
        v_rz := (v_zi & v_s) >> (ffs(v_s::bit(64)) - 1);
        
        v_index := (v_index << 3) | (v_rx << 2) | (v_ry << 1) | v_rz;
        
        v_s := v_s / 2;
    END LOOP;
    
    RETURN v_index;
END;
$$;

COMMENT ON FUNCTION hilbert_index_3d(REAL, REAL, REAL, INTEGER) IS 
'Compute 3D Hilbert curve index for space-filling optimization.
Maps 3D coordinates to 1D index preserving spatial locality.';


-- Helper function to add Hilbert index to atoms
CREATE OR REPLACE FUNCTION update_hilbert_indexes()
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_count BIGINT := 0;
BEGIN
    UPDATE atom
    SET metadata = metadata || jsonb_build_object(
        'hilbert_index', 
        hilbert_index_3d(
            ST_X(spatial_key),
            ST_Y(spatial_key),
            ST_Z(spatial_key)
        )
    )
    WHERE spatial_key IS NOT NULL
      AND (metadata->>'hilbert_index') IS NULL;
    
    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$;

COMMENT ON FUNCTION update_hilbert_indexes() IS 
'Batch update Hilbert curve indexes for all spatial atoms.
Returns number of atoms updated.';
