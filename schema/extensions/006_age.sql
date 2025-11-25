-- ============================================================================
-- Apache AGE Extension for Provenance Graph
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- CQRS Pattern: PostgreSQL (Write/Command) + AGE (Read/Provenance)
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS age CASCADE;

-- Set search path to include AGE catalog
SET search_path = ag_catalog, "$user", public;

COMMENT ON EXTENSION age IS 
'Apache AGE graph database extension for provenance tracking and metacognition.
Enables Cypher queries for deep lineage traversal that would be O(n²) in SQL CTEs.';
