-- ============================================================================
-- Natural Neighbor Interpolation
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Interpolate atom position using natural neighbors (Voronoi-based)
-- Use: Smooth spatial positioning, filling gaps in semantic space
-- ============================================================================

CREATE OR REPLACE FUNCTION natural_neighbor_interpolation(
    p_query_position GEOMETRY,
    p_neighbor_atom_ids BIGINT[]
)
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_weights REAL[];
    v_total_weight REAL := 0.0;
    v_result_x REAL := 0.0;
    v_result_y REAL := 0.0;
    v_result_z REAL := 0.0;
    v_neighbor_pos GEOMETRY;
    v_distance REAL;
    v_weight REAL;
BEGIN
    -- Compute inverse distance weights (IDW approximation of natural neighbor)
    FOR i IN 1..ARRAY_LENGTH(p_neighbor_atom_ids, 1) LOOP
        SELECT spatial_key INTO v_neighbor_pos
        FROM atom
        WHERE atom_id = p_neighbor_atom_ids[i];
        
        IF v_neighbor_pos IS NOT NULL THEN
            v_distance := ST_Distance(p_query_position, v_neighbor_pos);
            
            IF v_distance < 0.001 THEN
                -- Query point coincides with neighbor
                RETURN v_neighbor_pos;
            END IF;
            
            -- Inverse distance weighting: w = 1/d²
            v_weight := 1.0 / (v_distance * v_distance);
            v_weights := ARRAY_APPEND(v_weights, v_weight);
            v_total_weight := v_total_weight + v_weight;
        END IF;
    END LOOP;
    
    IF v_total_weight = 0.0 THEN
        RETURN NULL;
    END IF;
    
    -- Weighted average of neighbor positions
    FOR i IN 1..ARRAY_LENGTH(p_neighbor_atom_ids, 1) LOOP
        SELECT spatial_key INTO v_neighbor_pos
        FROM atom
        WHERE atom_id = p_neighbor_atom_ids[i];
        
        IF v_neighbor_pos IS NOT NULL THEN
            v_weight := v_weights[i] / v_total_weight;
            v_result_x := v_result_x + v_weight * ST_X(v_neighbor_pos);
            v_result_y := v_result_y + v_weight * ST_Y(v_neighbor_pos);
            v_result_z := v_result_z + v_weight * ST_Z(v_neighbor_pos);
        END IF;
    END LOOP;
    
    RETURN ST_MakePoint(v_result_x, v_result_y, v_result_z);
END;
$$;

COMMENT ON FUNCTION natural_neighbor_interpolation(GEOMETRY, BIGINT[]) IS 
'Natural neighbor interpolation: smooth spatial positioning using Voronoi-based weighting.
Approximated via inverse distance weighting (IDW).';
