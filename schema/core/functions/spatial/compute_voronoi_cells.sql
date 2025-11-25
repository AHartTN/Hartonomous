-- ============================================================================
-- Voronoi Tessellation for Semantic Space Partitioning
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Partition semantic space into Voronoi cells around concept clusters
-- Use: Identify semantic neighborhoods and cluster boundaries
-- ============================================================================

CREATE OR REPLACE FUNCTION compute_voronoi_cells(
    p_sample_atoms BIGINT[],  -- Array of landmark atom_ids
    p_grid_resolution REAL DEFAULT 0.5
)
RETURNS TABLE(
    atom_id BIGINT,
    voronoi_cell GEOMETRY,
    neighbor_atoms BIGINT[]
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_extent GEOMETRY;
    v_points GEOMETRY[];
BEGIN
    -- Get spatial extent of all atoms
    SELECT ST_Extent(spatial_key) INTO v_extent
    FROM atom
    WHERE atom_id = ANY(p_sample_atoms)
      AND spatial_key IS NOT NULL;
    
    IF v_extent IS NULL THEN
        RAISE EXCEPTION 'No spatial atoms found in sample set';
    END IF;
    
    -- Compute Voronoi using PostGIS
    RETURN QUERY
    WITH landmarks AS (
        SELECT atom_id, spatial_key
        FROM atom
        WHERE atom_id = ANY(p_sample_atoms)
          AND spatial_key IS NOT NULL
    ),
    voronoi AS (
        SELECT 
            (ST_Dump(ST_VoronoiPolygons(ST_Collect(spatial_key)))).geom as cell,
            spatial_key
        FROM landmarks
    )
    SELECT 
        l.atom_id,
        v.cell,
        ARRAY(
            SELECT a.atom_id 
            FROM atom a
            WHERE ST_Contains(v.cell, a.spatial_key)
              AND a.atom_id != l.atom_id
            LIMIT 1000
        ) as neighbors
    FROM voronoi v
    JOIN landmarks l ON ST_Contains(v.cell, l.spatial_key);
    
END;
$$;

COMMENT ON FUNCTION compute_voronoi_cells(BIGINT[], REAL) IS 
'Compute Voronoi tessellation of semantic space around landmark atoms.
Returns Voronoi cells and atoms contained within each cell.';
