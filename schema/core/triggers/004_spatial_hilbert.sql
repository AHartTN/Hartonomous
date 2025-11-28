-- ============================================================================
-- SPATIAL HILBERT INDEX TRIGGER
-- Automatically compute and update M coordinate (Hilbert index) when spatial_key changes
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- Update M coordinate with Hilbert index when spatial_key is set/changed
CREATE OR REPLACE FUNCTION update_hilbert_m_coordinate()
RETURNS TRIGGER AS $$
DECLARE
    v_x DOUBLE PRECISION;
    v_y DOUBLE PRECISION;
    v_z DOUBLE PRECISION;
    v_hilbert_index BIGINT;
BEGIN
    -- Only process if spatial_key is being set and has XYZ coordinates
    IF NEW.spatial_key IS NOT NULL THEN
        v_x := ST_X(NEW.spatial_key);
        v_y := ST_Y(NEW.spatial_key);
        v_z := ST_Z(NEW.spatial_key);
        
        -- Compute Hilbert index from (X,Y,Z)
        v_hilbert_index := hilbert_index_3d(v_x, v_y, v_z, 10);
        
        -- If M coordinate is not set or doesn't match computed Hilbert, update it
        IF ST_M(NEW.spatial_key) IS NULL OR 
           ST_M(NEW.spatial_key) != v_hilbert_index::DOUBLE PRECISION THEN
            NEW.spatial_key := ST_MakePoint(v_x, v_y, v_z, v_hilbert_index::DOUBLE PRECISION);
        END IF;
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Apply to atom table
DROP TRIGGER IF EXISTS trigger_atom_hilbert_index ON atom;
CREATE TRIGGER trigger_atom_hilbert_index
    BEFORE INSERT OR UPDATE OF spatial_key ON atom
    FOR EACH ROW 
    WHEN (NEW.spatial_key IS NOT NULL)
    EXECUTE FUNCTION update_hilbert_m_coordinate();

-- Apply to atom_composition table (for local coordinate frames)
DROP TRIGGER IF EXISTS trigger_composition_hilbert_index ON atom_composition;
CREATE TRIGGER trigger_composition_hilbert_index
    BEFORE INSERT OR UPDATE OF spatial_key ON atom_composition
    FOR EACH ROW 
    WHEN (NEW.spatial_key IS NOT NULL)
    EXECUTE FUNCTION update_hilbert_m_coordinate();

-- ============================================================================
-- COMMENTS
-- ============================================================================

COMMENT ON FUNCTION update_hilbert_m_coordinate() IS 
'Automatically compute and set M coordinate (Hilbert index) from (X,Y,Z) coordinates.
Ensures dual indexing strategy works: GiST on XYZ for exact, B-tree on M for approximate.
Triggered on INSERT/UPDATE of spatial_key in atom and atom_composition tables.';
