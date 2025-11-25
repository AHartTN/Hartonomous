-- ============================================================================
-- Compress Uniform Region via Hilbert Range
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- RLE compression: consecutive Hilbert indexes with same value
-- ============================================================================

CREATE OR REPLACE FUNCTION compress_uniform_hilbert_region(
    p_hilbert_start BIGINT,
    p_hilbert_end BIGINT,
    p_r INTEGER,
    p_g INTEGER,
    p_b INTEGER,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_atom_id BIGINT;
    v_color_position GEOMETRY;
    v_region_bytes BYTEA;
BEGIN
    v_color_position := ST_MakePoint(p_r, p_g, p_b);
    
    -- Pack as: start(8 bytes) + end(8 bytes) + RGB(3 bytes) = 19 bytes
    v_region_bytes := int8send(p_hilbert_start) || 
                      int8send(p_hilbert_end) ||
                      int2send(p_r::SMALLINT) ||
                      int2send(p_g::SMALLINT) ||
                      int2send(p_b::SMALLINT);
    
    v_atom_id := atomize_value(
        v_region_bytes,
        'Hilbert[' || p_hilbert_start || '..' || p_hilbert_end || ']?RGB(' || p_r || ',' || p_g || ',' || p_b || ')',
        p_metadata || jsonb_build_object(
            'modality', 'hilbert_region',
            'hilbert_start', p_hilbert_start,
            'hilbert_end', p_hilbert_end,
            'region_size', p_hilbert_end - p_hilbert_start + 1,
            'r', p_r,
            'g', p_g,
            'b', p_b,
            'compression_type', 'RLE'
        )
    );
    
    UPDATE atom 
    SET spatial_key = v_color_position 
    WHERE atom_id = v_atom_id;
    
    RETURN v_atom_id;
END;
$$;

COMMENT ON FUNCTION compress_uniform_hilbert_region(BIGINT, BIGINT, INTEGER, INTEGER, INTEGER, JSONB) IS 
'RLE compression: store Hilbert range instead of individual pixels. 
Massive savings for uniform regions (sky, solid colors).';
