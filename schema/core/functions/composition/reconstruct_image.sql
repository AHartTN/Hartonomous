-- ============================================================================
-- Reconstruct Image from Pixel Atoms
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Inverse operation: rebuild image from atomized pixels
-- ============================================================================

CREATE OR REPLACE FUNCTION reconstruct_image(p_pixel_atom_ids BIGINT[])
RETURNS TABLE(x INTEGER, y INTEGER, r INTEGER, g INTEGER, b INTEGER)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT 
        (a.metadata->>'x')::INTEGER,
        (a.metadata->>'y')::INTEGER,
        (a.metadata->>'r')::INTEGER,
        (a.metadata->>'g')::INTEGER,
        (a.metadata->>'b')::INTEGER
    FROM atom a
    WHERE a.atom_id = ANY(p_pixel_atom_ids)
    ORDER BY 
        (a.metadata->>'y')::INTEGER,
        (a.metadata->>'x')::INTEGER;
END;
$$;

COMMENT ON FUNCTION reconstruct_image(BIGINT[]) IS 
'Reconstruct image from pixel atom IDs. Returns ordered (x,y,r,g,b) tuples.';
