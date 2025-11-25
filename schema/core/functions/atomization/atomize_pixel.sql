-- ============================================================================
-- Atomize RGB Pixel
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Single RGB pixel as POINTZ(R, G, B) with Hilbert indexing
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_pixel(
    p_r INTEGER,
    p_g INTEGER,
    p_b INTEGER,
    p_x INTEGER,
    p_y INTEGER,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_color_position GEOMETRY;
    v_hilbert_idx BIGINT;
    v_pixel_bytes BYTEA;
BEGIN
    -- Validate RGB range (helper)
    PERFORM validate_rgb(p_r, p_g, p_b);
    
    -- RGB as 3D point in color space
    v_color_position := ST_MakePoint(p_r, p_g, p_b);
    
    -- Hilbert curve index for spatial locality (helper)
    v_hilbert_idx := rgb_to_hilbert(p_r, p_g, p_b);
    
    -- Pack RGB as 3 bytes
    v_pixel_bytes := int2send(p_r::SMALLINT) || 
                     int2send(p_g::SMALLINT) || 
                     int2send(p_b::SMALLINT);
    
    -- Atomize with spatial position (helper)
    RETURN atomize_with_spatial_key(
        v_pixel_bytes,
        v_color_position,
        'RGB(' || p_r || ',' || p_g || ',' || p_b || ')',
        p_metadata || jsonb_build_object(
            'modality', 'pixel',
            'r', p_r,
            'g', p_g,
            'b', p_b,
            'x', p_x,
            'y', p_y,
            'hilbert_index', v_hilbert_idx
        )
    );
END;
$$;

COMMENT ON FUNCTION atomize_pixel(INTEGER, INTEGER, INTEGER, INTEGER, INTEGER, JSONB) IS 
'Atomize single RGB pixel as POINTZ(R,G,B) with Hilbert curve indexing for color similarity.';
