-- ============================================================================
-- PostgreSQL Extensions Configuration
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- PostGIS: Spatial and geographic objects for PostgreSQL
-- Provides GEOMETRY types, spatial indexing (GIST R-tree), and spatial functions
CREATE EXTENSION IF NOT EXISTS postgis;

COMMENT ON EXTENSION postgis IS 
'PostGIS geometry and geography spatial types and functions';
