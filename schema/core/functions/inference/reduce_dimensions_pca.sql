-- ============================================================================
-- Dimensionality Reduction (PCA via PL/Python)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Reduce high-dimensional vectors to 3D for spatial indexing
-- ============================================================================

CREATE OR REPLACE FUNCTION reduce_dimensions_pca(
    p_atom_ids BIGINT[],
    p_n_components INTEGER DEFAULT 3
)
RETURNS TABLE(
    atom_id BIGINT,
    reduced_position GEOMETRY
)
LANGUAGE plpython3u
AS $$
import numpy as np
from sklearn.decomposition import PCA

# Fetch atom positions
plan = plpy.prepare("""
    SELECT atom_id, 
           ST_X(spatial_key) AS x,
           ST_Y(spatial_key) AS y,
           ST_Z(spatial_key) AS z
    FROM atom
    WHERE atom_id = ANY($1)
      AND spatial_key IS NOT NULL
""", ["bigint[]"])

result = plpy.execute(plan, [p_atom_ids])

if len(result) == 0:
    return []

# Build matrix
atom_ids = [row['atom_id'] for row in result]
positions = np.array([[row['x'], row['y'], row['z']] for row in result])

# PCA transformation
pca = PCA(n_components=p_n_components)
reduced = pca.fit_transform(positions)

# Return as geometry
output = []
for i, atom_id in enumerate(atom_ids):
    x, y, z = reduced[i] if p_n_components == 3 else (*reduced[i], 0.0)
    output.append({
        'atom_id': atom_id,
        'reduced_position': f'POINT({x} {y} {z})'
    })

return output
$$;

COMMENT ON FUNCTION reduce_dimensions_pca(BIGINT[], INTEGER) IS 
'PCA dimensionality reduction via scikit-learn in-database.
Transforms high-dim atom positions to 3D for spatial indexing.
Use for: embedding compression, visualization, feature extraction.';
