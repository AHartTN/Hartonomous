-- ============================================================================
-- Atomize Image (PIXEL-LEVEL)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Decompose image into individual PIXEL atoms
-- Returns array of pixel atom_ids
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_image(
    p_pixels INTEGER[][][],  -- [row][col][channel] where channel is R,G,B
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT[]
LANGUAGE plpgsql
AS $$
DECLARE
    v_atom_ids BIGINT[];
    v_pixel_id BIGINT;
    v_rows INTEGER;
    v_cols INTEGER;
    v_r INTEGER;
    v_g INTEGER;
    v_b INTEGER;
BEGIN
    v_rows := array_length(p_pixels, 1);
    v_cols := array_length(p_pixels, 2);
    
    -- Atomize EACH pixel individually
    FOR row IN 1..v_rows LOOP
        FOR col IN 1..v_cols LOOP
            v_r := p_pixels[row][col][1];
            v_g := p_pixels[row][col][2];
            v_b := p_pixels[row][col][3];
            
            -- Each pixel is a separate atom
            v_pixel_id := atomize_pixel(
                v_r, v_g, v_b,
                col, row,
                p_metadata || jsonb_build_object(
                    'image_width', v_cols,
                    'image_height', v_rows
                )
            );
            
            v_atom_ids := array_append(v_atom_ids, v_pixel_id);
        END LOOP;
    END LOOP;
    
    RETURN v_atom_ids;
END;
$$;

COMMENT ON FUNCTION atomize_image(INTEGER[][][], JSONB) IS 
'Atomize image at PIXEL level. Each RGB pixel becomes a separate atom with Hilbert indexing.
Returns ordered array of pixel atom_ids.';
