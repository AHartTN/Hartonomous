-- ============================================================================
-- Gram-Schmidt Orthogonalization
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Orthogonalize a set of vectors (atoms in semantic space)
-- Use: Create orthonormal basis for dimensionality reduction, PCA-like transforms
-- ============================================================================

CREATE OR REPLACE FUNCTION gram_schmidt_orthogonalize(
    p_atom_ids BIGINT[]
)
RETURNS TABLE(
    atom_id BIGINT,
    original_position GEOMETRY,
    orthogonalized_position GEOMETRY
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_vectors GEOMETRY[];
    v_ortho_vectors GEOMETRY[];
    v_current GEOMETRY;
    v_sum_x REAL;
    v_sum_y REAL;
    v_sum_z REAL;
    v_proj_coef REAL;
    v_mag REAL;
BEGIN
    -- Load original vectors
    SELECT ARRAY_AGG(spatial_key ORDER BY atom_id)
    INTO v_vectors
    FROM atom
    WHERE atom_id = ANY(p_atom_ids)
      AND spatial_key IS NOT NULL;
    
    IF v_vectors IS NULL OR ARRAY_LENGTH(v_vectors, 1) = 0 THEN
        RAISE EXCEPTION 'No spatial atoms found';
    END IF;
    
    -- Gram-Schmidt process
    FOR i IN 1..ARRAY_LENGTH(v_vectors, 1) LOOP
        v_current := v_vectors[i];
        v_sum_x := ST_X(v_current);
        v_sum_y := ST_Y(v_current);
        v_sum_z := ST_Z(v_current);
        
        -- Subtract projections onto all previous orthogonal vectors
        FOR j IN 1..i-1 LOOP
            -- Projection coefficient: (v · u_j) / (u_j · u_j) using helper
            v_proj_coef := dot_product_3d(v_current, v_ortho_vectors[j]) / 
                          NULLIF(dot_product_3d(v_ortho_vectors[j], v_ortho_vectors[j]), 0);
            
            -- Subtract projection
            v_sum_x := v_sum_x - (v_proj_coef * ST_X(v_ortho_vectors[j]));
            v_sum_y := v_sum_y - (v_proj_coef * ST_Y(v_ortho_vectors[j]));
            v_sum_z := v_sum_z - (v_proj_coef * ST_Z(v_ortho_vectors[j]));
        END LOOP;
        
        -- Normalize (creates orthonormal basis) using helper
        v_mag := SQRT(v_sum_x * v_sum_x + v_sum_y * v_sum_y + v_sum_z * v_sum_z);
        IF v_mag > 0.001 THEN
            v_sum_x := v_sum_x / v_mag;
            v_sum_y := v_sum_y / v_mag;
            v_sum_z := v_sum_z / v_mag;
        END IF;
        
        v_ortho_vectors := ARRAY_APPEND(v_ortho_vectors, ST_MakePoint(v_sum_x, v_sum_y, v_sum_z));
    END LOOP;
    
    -- Return results
    RETURN QUERY
    SELECT 
        p_atom_ids[i],
        v_vectors[i],
        v_ortho_vectors[i]
    FROM generate_subscripts(p_atom_ids, 1) AS i;
END;
$$;

COMMENT ON FUNCTION gram_schmidt_orthogonalize(BIGINT[]) IS 
'Gram-Schmidt orthogonalization: create orthonormal basis from atom positions.
Use for: dimensionality reduction, principal component analysis, basis transforms.';
