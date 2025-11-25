-- ============================================================================
-- Detect Compressible Hilbert Regions
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Find consecutive Hilbert indexes with same/similar values for RLE
-- ============================================================================

CREATE OR REPLACE FUNCTION detect_compressible_regions(
    p_modality TEXT DEFAULT 'pixel',
    p_similarity_threshold REAL DEFAULT 0.01
)
RETURNS TABLE(
    hilbert_start BIGINT,
    hilbert_end BIGINT,
    atom_count INTEGER,
    representative_atom_id BIGINT,
    compression_ratio REAL
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH hilbert_sequence AS (
        SELECT 
            atom_id,
            (metadata->>'hilbert_index')::BIGINT AS h_idx,
            spatial_key,
            LAG((metadata->>'hilbert_index')::BIGINT) OVER (ORDER BY (metadata->>'hilbert_index')::BIGINT) AS prev_h_idx,
            LAG(spatial_key) OVER (ORDER BY (metadata->>'hilbert_index')::BIGINT) AS prev_position
        FROM atom
        WHERE metadata->>'modality' = p_modality
          AND metadata->>'hilbert_index' IS NOT NULL
    ),
    run_boundaries AS (
        SELECT 
            atom_id,
            h_idx,
            spatial_key,
            -- New run starts when gap > 1 OR color changes significantly
            CASE 
                WHEN prev_h_idx IS NULL THEN 1
                WHEN h_idx - prev_h_idx > 1 THEN 1
                WHEN ST_Distance(spatial_key, prev_position) > p_similarity_threshold THEN 1
                ELSE 0
            END AS is_run_start
        FROM hilbert_sequence
    ),
    run_groups AS (
        SELECT 
            atom_id,
            h_idx,
            spatial_key,
            SUM(is_run_start) OVER (ORDER BY h_idx) AS run_id
        FROM run_boundaries
    )
    SELECT 
        MIN(h_idx) AS h_start,
        MAX(h_idx) AS h_end,
        COUNT(*)::INTEGER AS count,
        MIN(atom_id) AS representative,
        (COUNT(*)::REAL / 1.0) AS ratio  -- Original count vs 1 compressed atom
    FROM run_groups
    GROUP BY run_id
    HAVING COUNT(*) > 10  -- Only compress runs of 10+ similar values
    ORDER BY COUNT(*) DESC;
END;
$$;

COMMENT ON FUNCTION detect_compressible_regions(TEXT, REAL) IS 
'Detect consecutive Hilbert indexes with same/similar values for RLE compression.';
