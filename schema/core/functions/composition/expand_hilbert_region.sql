-- ============================================================================
-- Expand Hilbert Region to Individual Pixels
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Decompress RLE region back to pixel list
-- ============================================================================

CREATE OR REPLACE FUNCTION expand_hilbert_region(p_region_atom_id BIGINT)
RETURNS TABLE(hilbert_index BIGINT, x INTEGER, y INTEGER, r INTEGER, g INTEGER, b INTEGER)
LANGUAGE plpgsql
AS $$
DECLARE
    v_h_start BIGINT;
    v_h_end BIGINT;
    v_r INTEGER;
    v_g INTEGER;
    v_b INTEGER;
    v_width INTEGER := 1024;  -- Assume 1024x1024 image
BEGIN
    -- Get compressed region parameters
    SELECT 
        (metadata->>'hilbert_start')::BIGINT,
        (metadata->>'hilbert_end')::BIGINT,
        (metadata->>'r')::INTEGER,
        (metadata->>'g')::INTEGER,
        (metadata->>'b')::INTEGER
    INTO v_h_start, v_h_end, v_r, v_g, v_b
    FROM atom
    WHERE atom_id = p_region_atom_id;
    
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Region atom % not found', p_region_atom_id;
    END IF;
    
    -- Generate all Hilbert indexes in range and decode to x,y coordinates
    -- PROPER INVERSE HILBERT TRANSFORM using hilbert_decode_3d
    RETURN QUERY
    SELECT
        h_idx AS hilbert_index,
        -- Decode Hilbert index to normalized coordinates, then scale to image dimensions
        FLOOR((SELECT x FROM hilbert_decode_3d(h_idx, 10)) * v_width)::INTEGER AS x,
        FLOOR((SELECT y FROM hilbert_decode_3d(h_idx, 10)) * v_width)::INTEGER AS y,
        v_r AS r,
        v_g AS g,
        v_b AS b
    FROM generate_series(v_h_start, v_h_end) AS h_idx;
END;
$$;

COMMENT ON FUNCTION expand_hilbert_region(BIGINT) IS
'Expand RLE-compressed Hilbert region back to individual pixels.
Uses hilbert_decode_3d() to convert Hilbert indexes back to (x,y) pixel coordinates.';
