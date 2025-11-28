-- ============================================================================
-- Hilbert Curve Space-Filling for Spatial Indexing Optimization
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Map 3D coordinates to 1D Hilbert curve for better locality
-- Use: Optimize spatial queries and improve cache performance
-- ============================================================================

CREATE OR REPLACE FUNCTION hilbert_index_3d(
    p_x DOUBLE PRECISION,
    p_y DOUBLE PRECISION,
    p_z DOUBLE PRECISION,
    p_order INTEGER DEFAULT 10  -- Hilbert curve order (10 = 1024^3 resolution)
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
    
    -- Compute 3D Hilbert index using bit interleaving (Z-order/Morton code approximation)
    -- This is a simplified version - true Hilbert curve requires rotation matrices
    -- But provides good spatial locality for our dual-indexing strategy
    v_s := v_max_coord / 2;
    v_n := 0;
    WHILE v_s > 0 LOOP
        v_rx := CASE WHEN (v_xi & v_s) > 0 THEN 1 ELSE 0 END;
        v_ry := CASE WHEN (v_yi & v_s) > 0 THEN 1 ELSE 0 END;
        v_rz := CASE WHEN (v_zi & v_s) > 0 THEN 1 ELSE 0 END;
        
        -- Interleave bits: Z Y X for each level
        v_index := (v_index << 3) | (v_rz << 2) | (v_ry << 1) | v_rx;
        
        v_s := v_s / 2;
        v_n := v_n + 1;
    END LOOP;
    
    RETURN v_index;
END;
$$;

COMMENT ON FUNCTION hilbert_index_3d(DOUBLE PRECISION, DOUBLE PRECISION, DOUBLE PRECISION, INTEGER) IS 
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
