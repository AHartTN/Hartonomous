-- ============================================================================
-- Atomize 3D Voxel (Sparse Point Cloud / Video)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- 3D voxel grid with Hilbert indexing, skip empty voxels
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_voxel(
    p_x INTEGER,
    p_y INTEGER,
    p_z INTEGER,
    p_value REAL,
    p_threshold REAL DEFAULT 0.001,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_hilbert_idx BIGINT;
    v_position GEOMETRY;
    v_voxel_bytes BYTEA;
BEGIN
    -- Skip empty voxels (sparse storage)
    IF ABS(p_value) < p_threshold THEN
        RETURN NULL;
    END IF;
    
    -- 3D Hilbert curve for spatial locality
    v_hilbert_idx := hilbert_index_3d(
        p_x::REAL,
        p_y::REAL,
        p_z::REAL,
        10  -- 1024³ resolution
    );
    
    v_position := ST_MakePoint(p_x, p_y, p_z);
    
    -- Pack as: x(2) + y(2) + z(2) + value(4) = 10 bytes
    v_voxel_bytes := int2send(p_x::SMALLINT) ||
                     int2send(p_y::SMALLINT) ||
                     int2send(p_z::SMALLINT) ||
                     float4send(p_value::REAL);
    
    -- Atomize with spatial position (helper)
    RETURN atomize_with_spatial_key(
        v_voxel_bytes,
        v_position,
        'Voxel[' || p_x || ',' || p_y || ',' || p_z || ']=' || p_value,
        p_metadata || jsonb_build_object(
            'modality', 'voxel',
            'x', p_x,
            'y', p_y,
            'z', p_z,
            'value', p_value,
            'hilbert_index', v_hilbert_idx,
            'sparse', true
        )
    );
END;
$$;

COMMENT ON FUNCTION atomize_voxel(INTEGER, INTEGER, INTEGER, REAL, REAL, JSONB) IS 
'Atomize 3D voxel with Hilbert indexing. Sparse storage: skip voxels below threshold.
Use for: point clouds, volumetric video, 3D medical imaging.';
