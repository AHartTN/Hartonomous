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
    
    -- Generate all Hilbert indexes in range
    -- TODO: Implement inverse Hilbert transform (Hilbert ? x,y)
    -- For now, return Hilbert indexes only
    RETURN QUERY
    SELECT 
        h_idx AS hilbert_index,
        0 AS x,  -- Placeholder - needs inverse Hilbert
        0 AS y,  -- Placeholder - needs inverse Hilbert  
        v_r AS r,
        v_g AS g,
        v_b AS b
    FROM generate_series(v_h_start, v_h_end) AS h_idx;
END;
$$;

COMMENT ON FUNCTION expand_hilbert_region(BIGINT) IS 
'Expand RLE-compressed Hilbert region back to individual pixels.
TODO: Implement inverse Hilbert curve transform for x,y coordinates.';
