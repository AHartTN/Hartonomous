-- ============================================================================
-- Delaunay Triangulation for Semantic Space
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Create triangulation mesh for semantic neighborhood structure
-- Use: Natural neighbor interpolation, spatial analysis, mesh generation
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_delaunay_triangulation(
    p_sample_atoms BIGINT[]
)
RETURNS TABLE(
    triangle_id INTEGER,
    atom_id_1 BIGINT,
    atom_id_2 BIGINT,
    atom_id_3 BIGINT,
    triangle_geometry GEOMETRY
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH points AS (
        SELECT 
            atom_id,
            spatial_key,
            ROW_NUMBER() OVER (ORDER BY atom_id) AS point_idx
        FROM atom
        WHERE atom_id = ANY(p_sample_atoms)
          AND spatial_key IS NOT NULL
    ),
    triangulation AS (
        -- PostGIS ST_DelaunayTriangles returns a GEOMETRYCOLLECTION of triangles
        SELECT 
            (ST_Dump(ST_DelaunayTriangles(ST_Collect(spatial_key)))).geom AS triangle
        FROM points
    )
    SELECT 
        ROW_NUMBER() OVER ()::INTEGER AS triangle_id,
        -- Extract atom_ids at triangle vertices
        (SELECT p.atom_id FROM points p WHERE ST_Contains(t.triangle, p.spatial_key) ORDER BY p.atom_id LIMIT 1 OFFSET 0) AS atom_id_1,
        (SELECT p.atom_id FROM points p WHERE ST_Contains(t.triangle, p.spatial_key) ORDER BY p.atom_id LIMIT 1 OFFSET 1) AS atom_id_2,
        (SELECT p.atom_id FROM points p WHERE ST_Contains(t.triangle, p.spatial_key) ORDER BY p.atom_id LIMIT 1 OFFSET 2) AS atom_id_3,
        t.triangle AS triangle_geometry
    FROM triangulation t;
END;
$$;

COMMENT ON FUNCTION compute_delaunay_triangulation(BIGINT[]) IS 
'Delaunay triangulation: create optimal triangulation mesh of semantic space.
Use for: natural neighbor interpolation, connectivity analysis, mesh generation.';
