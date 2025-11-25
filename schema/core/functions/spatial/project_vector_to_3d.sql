-- ============================================================================
-- Project Vector to 3D Geometry (UMAP/PCA)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Project high-dimensional vectors to 3D POINTZ for spatial indexing
-- ============================================================================

CREATE OR REPLACE FUNCTION project_vector_to_3d(
    p_vector REAL[],
    p_method TEXT DEFAULT 'pca'
)
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
DECLARE
    v_dim INTEGER;
    v_x REAL;
    v_y REAL;
    v_z REAL;
BEGIN
    v_dim := ARRAY_LENGTH(p_vector, 1);
    
    IF v_dim IS NULL OR v_dim = 0 THEN
        RETURN NULL;
    END IF;
    
    -- Simple projection methods (PCA requires batch processing)
    IF p_method = 'simple' THEN
        -- Take first 3 dimensions
        v_x := COALESCE(p_vector[1], 0.0);
        v_y := COALESCE(p_vector[2], 0.0);
        v_z := COALESCE(p_vector[3], 0.0);
        
    ELSIF p_method = 'sum_projection' THEN
        -- Project onto 3 axes using weighted sums
        v_x := 0.0;
        v_y := 0.0;
        v_z := 0.0;
        FOR i IN 1..v_dim LOOP
            v_x := v_x + p_vector[i] * cos(i::REAL * 0.1);
            v_y := v_y + p_vector[i] * sin(i::REAL * 0.1);
            v_z := v_z + p_vector[i] * cos(i::REAL * 0.2);
        END LOOP;
        
    ELSE
        -- Default: normalize to first 3 dimensions
        v_x := COALESCE(p_vector[1], 0.0);
        v_y := COALESCE(p_vector[2], 0.0);
        v_z := COALESCE(p_vector[3], 0.0);
    END IF;
    
    -- Normalize to [-10, 10] range
    v_x := GREATEST(-10.0, LEAST(10.0, v_x));
    v_y := GREATEST(-10.0, LEAST(10.0, v_y));
    v_z := GREATEST(-10.0, LEAST(10.0, v_z));
    
    RETURN ST_MakePoint(v_x, v_y, v_z);
END;
$$;

COMMENT ON FUNCTION project_vector_to_3d(REAL[], TEXT) IS 
'Project high-dimensional vector to 3D POINTZ for spatial indexing.
Methods: simple (first 3), sum_projection (weighted), pca (requires batch).';
