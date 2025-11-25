-- ============================================================================
-- AGE Provenance Graph Schema
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Lineage tracking, causality analysis, error tracing
-- The "Why" behind every atom - metacognition layer
-- ============================================================================

-- Create provenance graph
SELECT ag_catalog.create_graph('provenance');

-- ============================================================================
-- Node Types (Vertices)
-- ============================================================================

-- Atom nodes (mirror of PostgreSQL atoms)
SELECT ag_catalog.create_vlabel('provenance', 'Atom');

-- Inference/Decision nodes
SELECT ag_catalog.create_vlabel('provenance', 'Inference');

-- Model nodes (GPT, Claude, Llama, etc.)
SELECT ag_catalog.create_vlabel('provenance', 'Model');

-- Session nodes (user interactions)
SELECT ag_catalog.create_vlabel('provenance', 'Session');

-- Error nodes (for debugging hallucinations)
SELECT ag_catalog.create_vlabel('provenance', 'Error');

-- OODA cycle nodes (autonomous decisions)
SELECT ag_catalog.create_vlabel('provenance', 'OODADecision');

-- ============================================================================
-- Edge Types (Relationships)
-- ============================================================================

-- Lineage: atom derived from another atom
SELECT ag_catalog.create_elabel('provenance', 'DERIVED_FROM');

-- Model usage: inference used specific model
SELECT ag_catalog.create_elabel('provenance', 'USED_MODEL');

-- Atom usage: inference consumed specific atom
SELECT ag_catalog.create_elabel('provenance', 'USED_ATOM');

-- Result: inference produced specific atom
SELECT ag_catalog.create_elabel('provenance', 'RESULTED_IN');

-- Session linkage: atom created in session
SELECT ag_catalog.create_elabel('provenance', 'CREATED_IN_SESSION');

-- Causality: decision caused action
SELECT ag_catalog.create_elabel('provenance', 'CAUSED');

-- Error linkage: error traced to poison atom
SELECT ag_catalog.create_elabel('provenance', 'TRACED_TO');

-- Composition: parent contains component
SELECT ag_catalog.create_elabel('provenance', 'COMPOSED_OF');

-- Relation: semantic relationship
SELECT ag_catalog.create_elabel('provenance', 'RELATES_TO');

COMMENT ON SCHEMA ag_graph_provenance IS 
'AGE provenance graph: tracks WHY every atom exists.
Enables O(1) lineage queries vs O(n²) recursive CTEs in PostgreSQL.
The "Strange Loop": Data ? Inference ? Data ? Inference...';
