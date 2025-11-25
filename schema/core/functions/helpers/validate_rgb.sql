-- ============================================================================
-- Helper: Validate RGB Range
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Validate RGB values are in 0-255 range
-- ============================================================================

CREATE OR REPLACE FUNCTION validate_rgb(p_r INTEGER, p_g INTEGER, p_b INTEGER)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_r < 0 OR p_r > 255 OR p_g < 0 OR p_g > 255 OR p_b < 0 OR p_b > 255 THEN
        RAISE EXCEPTION 'RGB values must be 0-255, got R=%, G=%, B=%', p_r, p_g, p_b;
    END IF;
END;
$$;

COMMENT ON FUNCTION validate_rgb(INTEGER, INTEGER, INTEGER) IS 
'Helper: validate RGB values are in 0-255 range.';
