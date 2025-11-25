-- ============================================================================
-- Find Similar Image Patches
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Query by spatial pattern in MULTIPOINT geometry
-- ============================================================================

CREATE OR REPLACE FUNCTION find_similar_image_patches(
    p_pattern GEOMETRY,
    p_limit INTEGER DEFAULT 10
)
RETURNS TABLE(
    atom_id BIGINT,
    canonical_text TEXT,
    similarity REAL,
    patch_geometry GEOMETRY
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT 
        a.atom_id,
        a.canonical_text,
        (1.0 / (1.0 + ST_Distance(a.spatial_key, p_pattern)))::REAL AS similarity,
        a.spatial_key AS patch_geometry
    FROM atom a
    WHERE a.metadata->>'modality' = 'image_patch'
      AND a.spatial_key IS NOT NULL
    ORDER BY ST_Distance(a.spatial_key, p_pattern) ASC
    LIMIT p_limit;
END;
$$;

COMMENT ON FUNCTION find_similar_image_patches(GEOMETRY, INTEGER) IS 
'Find similar image patches using spatial distance on MULTIPOINT geometry.';
