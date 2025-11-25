-- ============================================================================
-- Vector Atoms View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Expose embeddings as 3D projected geometry
-- ============================================================================

CREATE VIEW v_vector_atoms AS
SELECT 
    a.atom_id,
    a.canonical_text,
    a.spatial_key AS projected_position,
    ST_X(a.spatial_key) AS x,
    ST_Y(a.spatial_key) AS y,
    ST_Z(a.spatial_key) AS z,
    (a.metadata->>'original_dimensions')::INTEGER AS original_dimensions,
    a.metadata->>'projection_method' AS projection_method,
    a.reference_count,
    a.created_at,
    a.metadata
FROM atom a
WHERE a.metadata->>'modality' = 'embedding'
  AND a.spatial_key IS NOT NULL;

COMMENT ON VIEW v_vector_atoms IS 
'Vector embeddings projected to 3D POINTZ for spatial indexing.';
