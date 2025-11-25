-- ============================================================================
-- Helper: RGB to Hilbert Index
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Convert RGB to Hilbert curve index (common operation)
-- ============================================================================

CREATE OR REPLACE FUNCTION rgb_to_hilbert(p_r INTEGER, p_g INTEGER, p_b INTEGER)
RETURNS BIGINT
LANGUAGE plpgsql IMMUTABLE
AS $$
BEGIN
    RETURN hilbert_index_3d(
        (p_r - 128)::REAL / 12.8,  -- Normalize to [-10, 10]
        (p_g - 128)::REAL / 12.8,
        (p_b - 128)::REAL / 12.8,
        8  -- 256³ resolution
    );
END;
$$;

COMMENT ON FUNCTION rgb_to_hilbert(INTEGER, INTEGER, INTEGER) IS 
'Helper: convert RGB to Hilbert curve index for color similarity queries.';
