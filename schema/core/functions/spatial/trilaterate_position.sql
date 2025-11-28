-- ============================================================================
-- Trilateration (3D Position from Distances)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Compute 3D position from distances to 3+ known points
-- Use: Infer unknown atom position from semantic distances to known atoms
-- ============================================================================

CREATE OR REPLACE FUNCTION trilaterate_position(
    p_reference_atoms BIGINT[],  -- At least 3 atoms with known positions
    p_distances REAL[]            -- Distances from unknown atom to each reference
)
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_p1 GEOMETRY;
    v_p2 GEOMETRY;
    v_p3 GEOMETRY;
    v_d1 REAL;
    v_d2 REAL;
    v_d3 REAL;
    v_x REAL;
    v_y REAL;
    v_z REAL;
    v_i REAL;
    v_j REAL;
    v_ex_x REAL;
    v_ex_y REAL;
    v_ex_z REAL;
    v_ey_x REAL;
    v_ey_y REAL;
    v_ey_z REAL;
    v_ez_x REAL;
    v_ez_y REAL;
    v_ez_z REAL;
    v_d REAL;
    v_norm REAL;
BEGIN
    IF ARRAY_LENGTH(p_reference_atoms, 1) < 3 OR ARRAY_LENGTH(p_distances, 1) < 3 THEN
        RAISE EXCEPTION 'Trilateration requires at least 3 reference points';
    END IF;
    
    -- Get reference positions
    SELECT spatial_key INTO v_p1 FROM atom WHERE atom_id = p_reference_atoms[1];
    SELECT spatial_key INTO v_p2 FROM atom WHERE atom_id = p_reference_atoms[2];
    SELECT spatial_key INTO v_p3 FROM atom WHERE atom_id = p_reference_atoms[3];
    
    v_d1 := p_distances[1];
    v_d2 := p_distances[2];
    v_d3 := p_distances[3];
    
    -- Unit vector from p1 to p2
    v_ex_x := ST_X(v_p2) - ST_X(v_p1);
    v_ex_y := ST_Y(v_p2) - ST_Y(v_p1);
    v_ex_z := ST_Z(v_p2) - ST_Z(v_p1);
    v_d := SQRT(v_ex_x * v_ex_x + v_ex_y * v_ex_y + v_ex_z * v_ex_z);
    v_ex_x := v_ex_x / v_d;
    v_ex_y := v_ex_y / v_d;
    v_ex_z := v_ex_z / v_d;
    
    -- Signed magnitude of ex in direction of (p3 - p1)
    v_i := v_ex_x * (ST_X(v_p3) - ST_X(v_p1)) +
           v_ex_y * (ST_Y(v_p3) - ST_Y(v_p1)) +
           v_ex_z * (ST_Z(v_p3) - ST_Z(v_p1));
    
    -- Unit vector from p1 to p3 projection onto ex-plane
    v_ey_x := (ST_X(v_p3) - ST_X(v_p1)) - v_i * v_ex_x;
    v_ey_y := (ST_Y(v_p3) - ST_Y(v_p1)) - v_i * v_ex_y;
    v_ey_z := (ST_Z(v_p3) - ST_Z(v_p1)) - v_i * v_ex_z;
    v_norm := SQRT(v_ey_x * v_ey_x + v_ey_y * v_ey_y + v_ey_z * v_ey_z);
    v_ey_x := v_ey_x / v_norm;
    v_ey_y := v_ey_y / v_norm;
    v_ey_z := v_ey_z / v_norm;
    
    -- Cross product: ez = ex � ey
    v_ez_x := v_ex_y * v_ey_z - v_ex_z * v_ey_y;
    v_ez_y := v_ex_z * v_ey_x - v_ex_x * v_ey_z;
    v_ez_z := v_ex_x * v_ey_y - v_ex_y * v_ey_x;
    
    -- Signed magnitude of ey in direction of (p3 - p1)
    v_j := v_ey_x * (ST_X(v_p3) - ST_X(v_p1)) +
           v_ey_y * (ST_Y(v_p3) - ST_Y(v_p1)) +
           v_ey_z * (ST_Z(v_p3) - ST_Z(v_p1));
    
    -- Trilateration formulas
    v_x := (v_d1 * v_d1 - v_d2 * v_d2 + v_d * v_d) / (2 * v_d);
    v_y := (v_d1 * v_d1 - v_d3 * v_d3 + v_i * v_i + v_j * v_j) / (2 * v_j) - (v_i / v_j) * v_x;
    v_z := SQRT(GREATEST(0, v_d1 * v_d1 - v_x * v_x - v_y * v_y));  -- Positive z solution
    
    -- Transform back to original coordinate system
    -- Note: Returns POINTZ; trigger will compute M coordinate (Hilbert index)
    RETURN ST_MakePoint(
        ST_X(v_p1) + v_x * v_ex_x + v_y * v_ey_x + v_z * v_ez_x,
        ST_Y(v_p1) + v_x * v_ex_y + v_y * v_ey_y + v_z * v_ez_y,
        ST_Z(v_p1) + v_x * v_ex_z + v_y * v_ey_z + v_z * v_ez_z
    );
END;
$$;

COMMENT ON FUNCTION trilaterate_position(BIGINT[], REAL[]) IS 
'Trilateration: compute 3D position from distances to 3+ reference points.
Returns POINTZ; trigger automatically adds M coordinate (Hilbert index) → POINTZM.
Use for: inferring unknown atom positions from semantic distances.';
