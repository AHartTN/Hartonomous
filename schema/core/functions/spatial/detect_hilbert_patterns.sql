-- ============================================================================
-- Detect Compressible Patterns via Hilbert Auto-Correlation
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Find repeating patterns in Hilbert space (textures, periodic signals)
-- ============================================================================

CREATE OR REPLACE FUNCTION detect_hilbert_patterns(
    p_modality TEXT DEFAULT 'pixel',
    p_pattern_length INTEGER DEFAULT 256
)
RETURNS TABLE(
    pattern_start BIGINT,
    pattern_length INTEGER,
    repeat_count INTEGER,
    compression_factor REAL
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH hilbert_seq AS (
        SELECT 
            (metadata->>'hilbert_index')::BIGINT AS h_idx,
            spatial_key,
            ROW_NUMBER() OVER (ORDER BY (metadata->>'hilbert_index')::BIGINT) AS seq_num
        FROM atom
        WHERE metadata->>'modality' = p_modality
          AND metadata->>'hilbert_index' IS NOT NULL
    ),
    pattern_candidates AS (
        SELECT 
            s1.h_idx AS pattern_start,
            p_pattern_length AS length,
            COUNT(DISTINCT s2.seq_num / p_pattern_length) AS repeats
        FROM hilbert_seq s1
        JOIN hilbert_seq s2 ON 
            (s2.seq_num - s1.seq_num) % p_pattern_length = 0
            AND ST_Distance(s1.spatial_key, s2.spatial_key) < 0.1  -- Similar values
            AND s2.seq_num > s1.seq_num
        WHERE s1.seq_num <= 10000  -- Sample first 10K for performance
        GROUP BY s1.h_idx
    )
    SELECT 
        pattern_start,
        length,
        repeats,
        (repeats * length)::REAL / length AS factor
    FROM pattern_candidates
    WHERE repeats > 2  -- At least 2 repetitions
    ORDER BY repeats DESC
    LIMIT 100;
END;
$$;

COMMENT ON FUNCTION detect_hilbert_patterns(TEXT, INTEGER) IS 
'Detect repeating patterns in Hilbert space via auto-correlation.
Use for: texture compression, periodic signal detection, pattern-based RLE.';
