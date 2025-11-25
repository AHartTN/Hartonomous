-- ============================================================================
-- Pixel Atoms View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Individual RGB pixels in 3D color space with Hilbert indexing
-- ============================================================================

CREATE VIEW v_pixel_atoms AS
SELECT 
    a.atom_id,
    (a.metadata->>'r')::INTEGER AS r,
    (a.metadata->>'g')::INTEGER AS g,
    (a.metadata->>'b')::INTEGER AS b,
    (a.metadata->>'x')::INTEGER AS x,
    (a.metadata->>'y')::INTEGER AS y,
    a.spatial_key AS color_position,
    (a.metadata->>'hilbert_index')::BIGINT AS hilbert_index,
    a.canonical_text AS rgb_text,
    a.reference_count,
    a.created_at,
    a.metadata
FROM atom a
WHERE a.metadata->>'modality' = 'pixel';

CREATE INDEX ON v_pixel_atoms (hilbert_index);
CREATE INDEX ON v_pixel_atoms (r, g, b);

COMMENT ON VIEW v_pixel_atoms IS 
'Individual RGB pixels as atoms in 3D color space. Hilbert index preserves color similarity.';
