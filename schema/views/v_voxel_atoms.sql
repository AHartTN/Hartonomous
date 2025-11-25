-- ============================================================================
-- Sparse Voxel Grid View
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- 3D sparse voxel storage (point clouds, volumetric data)
-- ============================================================================

CREATE VIEW v_voxel_atoms AS
SELECT 
    a.atom_id,
    (a.metadata->>'x')::INTEGER AS x,
    (a.metadata->>'y')::INTEGER AS y,
    (a.metadata->>'z')::INTEGER AS z,
    (a.metadata->>'value')::REAL AS value,
    (a.metadata->>'hilbert_index')::BIGINT AS hilbert_index,
    a.spatial_key AS position,
    a.reference_count,
    a.created_at,
    a.metadata
FROM atom a
WHERE a.metadata->>'modality' = 'voxel';

CREATE INDEX ON v_voxel_atoms (hilbert_index);
CREATE INDEX ON v_voxel_atoms (x, y, z);

COMMENT ON VIEW v_voxel_atoms IS 
'Sparse 3D voxel grid with Hilbert indexing. Empty voxels not stored (implicit zeros).';
