-- ============================================================================
-- Find Similar Colors (Hilbert)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Query similar colors using Hilbert curve proximity
-- ============================================================================

CREATE OR REPLACE FUNCTION find_similar_colors_hilbert(
    p_r INTEGER,
    p_g INTEGER,
    p_b INTEGER,
    p_limit INTEGER DEFAULT 20
)
RETURNS TABLE(
    atom_id BIGINT,
    r INTEGER,
    g INTEGER,
    b INTEGER,
    hilbert_distance BIGINT,
    euclidean_distance REAL
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_target_hilbert BIGINT;
    v_target_position GEOMETRY;
BEGIN
    v_target_hilbert := hilbert_index_3d(
        (p_r - 128)::REAL / 12.8,
        (p_g - 128)::REAL / 12.8,
        (p_b - 128)::REAL / 12.8,
        8
    );
    
    v_target_position := ST_MakePoint(p_r, p_g, p_b);
    
    RETURN QUERY
    SELECT 
        a.atom_id,
        (a.metadata->>'r')::INTEGER,
        (a.metadata->>'g')::INTEGER,
        (a.metadata->>'b')::INTEGER,
        ABS((a.metadata->>'hilbert_index')::BIGINT - v_target_hilbert) AS h_dist,
        ST_Distance(a.spatial_key, v_target_position) AS e_dist
    FROM atom a
    WHERE a.metadata->>'modality' = 'pixel'
    ORDER BY ABS((a.metadata->>'hilbert_index')::BIGINT - v_target_hilbert)
    LIMIT p_limit;
END;
$$;

COMMENT ON FUNCTION find_similar_colors_hilbert(INTEGER, INTEGER, INTEGER, INTEGER) IS 
'Find similar RGB colors using Hilbert curve distance (preserves spatial locality in color space).';
