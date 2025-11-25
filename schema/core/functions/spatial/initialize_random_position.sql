-- ============================================================================
-- Random Position Initialization
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Initialize atoms in bounded random positions when no neighbors exist
-- ============================================================================

CREATE OR REPLACE FUNCTION initialize_random_position()
RETURNS GEOMETRY
LANGUAGE plpgsql
AS $$
BEGIN
    -- Initialize in bounded 3D space [-10, 10] for each dimension
    -- This provides a starting point for atoms without semantic neighbors
    RETURN ST_MakePoint(
        random() * 20 - 10,  -- X: [-10, 10]
        random() * 20 - 10,  -- Y: [-10, 10]
        random() * 20 - 10   -- Z: [-10, 10]
    );
END;
$$;

-- ============================================================================
-- Function Metadata
-- ============================================================================

COMMENT ON FUNCTION initialize_random_position() IS 
'Generate random 3D position in bounded space [-10, 10]³.
Used for initializing atoms when no semantic neighbors exist yet.';
