-- ============================================================================
-- Quadtree-like Compression via Hilbert Subdivision
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Hierarchical LOD: store coarse Hilbert regions, subdivide on demand
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_hilbert_lod(
    p_hilbert_start BIGINT,
    p_hilbert_end BIGINT,
    p_lod_level INTEGER,  -- 0 = finest, higher = coarser
    p_avg_color_atom_id BIGINT,
    p_variance REAL,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_atom_id BIGINT;
    v_lod_bytes BYTEA;
BEGIN
    -- Pack LOD metadata
    v_lod_bytes := int8send(p_hilbert_start) ||
                   int8send(p_hilbert_end) ||
                   int4send(p_lod_level) ||
                   int8send(p_avg_color_atom_id) ||
                   float4send(p_variance);
    
    v_atom_id := atomize_value(
        v_lod_bytes,
        'LOD' || p_lod_level || '[' || p_hilbert_start || '..' || p_hilbert_end || ']',
        p_metadata || jsonb_build_object(
            'modality', 'hilbert_lod',
            'hilbert_start', p_hilbert_start,
            'hilbert_end', p_hilbert_end,
            'lod_level', p_lod_level,
            'avg_color_atom_id', p_avg_color_atom_id,
            'variance', p_variance,
            'compression_type', 'LOD_quadtree'
        )
    );
    
    RETURN v_atom_id;
END;
$$;

COMMENT ON FUNCTION atomize_hilbert_lod(BIGINT, BIGINT, INTEGER, BIGINT, REAL, JSONB) IS 
'Quadtree-like LOD via Hilbert subdivision. High variance regions ? subdivide. Low variance ? compress.
Use for: progressive image loading, adaptive detail rendering.';
