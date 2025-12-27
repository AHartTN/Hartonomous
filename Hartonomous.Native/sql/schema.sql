-- Hartonomous Database Schema
-- PostGIS schema for atoms and compositions
-- IDEMPOTENT: Safe to run multiple times, preserves existing data

-- Extensions
CREATE EXTENSION IF NOT EXISTS postgis;

--------------------------------------------------------------------------------
-- ATOM TABLE
-- 
-- The fundamental unit of the system. Each row represents a single Unicode
-- codepoint with its 4D semantic position on the Hilbert curve.
--
-- The primary key is the 128-bit Hilbert curve index (hi, lo) which provides
-- locality-preserving indexing in 4D space.
--------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS atom (
    -- 128-bit Hilbert index (high/low 64-bit components)
    hilbert_high BIGINT NOT NULL,
    hilbert_low BIGINT NOT NULL,

    -- The Unicode codepoint (U+0000 to U+10FFFF)
    codepoint INTEGER,

    -- Number of direct children (0 for atoms)
    child_count SMALLINT NOT NULL DEFAULT 0,

    -- 4D semantic position for spatial queries
    -- X = Unicode page (block grouping)
    -- Y = Character type (letter, digit, symbol, etc.)
    -- Z = Base character (canonical form)
    -- M = Variant (case, diacritic, etc.)
    semantic_position GEOMETRY(POINTZM, 0),

    PRIMARY KEY (hilbert_high, hilbert_low)
);

--------------------------------------------------------------------------------
-- COMPOSITION TABLE
--
-- Binary compositions: each composition is (left, right).
-- Uses Hilbert indices for all references.
-- One row per composition - not normalized into parent/child relations.
--------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS composition (
    -- Composition's Hilbert index (Merkle hash of children)
    hilbert_high BIGINT NOT NULL,
    hilbert_low BIGINT NOT NULL,
    
    -- Left child's Hilbert index (can be atom or composition)
    left_high BIGINT NOT NULL,
    left_low BIGINT NOT NULL,
    
    -- Right child's Hilbert index (can be atom or composition)
    right_high BIGINT NOT NULL,
    right_low BIGINT NOT NULL,

    PRIMARY KEY (hilbert_high, hilbert_low)
);

--------------------------------------------------------------------------------
-- INDEXES
--------------------------------------------------------------------------------

-- Spatial index for semantic proximity queries
CREATE INDEX IF NOT EXISTS idx_atom_semantic_position 
    ON atom USING GIST (semantic_position);

-- Codepoint lookup index
CREATE INDEX IF NOT EXISTS idx_atom_codepoint 
    ON atom (codepoint) 
    WHERE codepoint IS NOT NULL;

-- Child lookup indexes for traversing compositions
CREATE INDEX IF NOT EXISTS idx_composition_left
    ON composition (left_high, left_low);

CREATE INDEX IF NOT EXISTS idx_composition_right
    ON composition (right_high, right_low);

--------------------------------------------------------------------------------
-- RELATIONSHIP TABLE
--
-- Weighted relationships: A → B with weight and trajectory.
-- Used for storing model weights, semantic links, knowledge graph edges.
--------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS relationship (
    -- Source atom/composition
    from_high BIGINT NOT NULL,
    from_low BIGINT NOT NULL,

    -- Target atom/composition
    to_high BIGINT NOT NULL,
    to_low BIGINT NOT NULL,

    -- Weight as numeric (for model weights, scores, etc.)
    weight DOUBLE PRECISION NOT NULL DEFAULT 1.0,

    -- Trajectory through semantic space (start → end as LINESTRINGZM)
    trajectory GEOMETRY(LINESTRINGZM, 0),

    -- Relationship type (for categorizing: model_weight, semantic_link, etc.)
    rel_type SMALLINT NOT NULL DEFAULT 0,

    -- Model/context identifier (which model or context this relationship belongs to)
    context_high BIGINT DEFAULT 0,
    context_low BIGINT DEFAULT 0,

    PRIMARY KEY (from_high, from_low, to_high, to_low, context_high, context_low)
);

-- Indexes for relationship queries
CREATE INDEX IF NOT EXISTS idx_relationship_from 
    ON relationship (from_high, from_low);
CREATE INDEX IF NOT EXISTS idx_relationship_to 
    ON relationship (to_high, to_low);
CREATE INDEX IF NOT EXISTS idx_relationship_context 
    ON relationship (context_high, context_low);
CREATE INDEX IF NOT EXISTS idx_relationship_weight 
    ON relationship (weight);
CREATE INDEX IF NOT EXISTS idx_relationship_trajectory 
    ON relationship USING GIST (trajectory);

--------------------------------------------------------------------------------
-- EXAMPLE QUERIES
--------------------------------------------------------------------------------

-- Find atoms semantically near 'a' (codepoint 97):
-- SELECT * FROM atom
-- WHERE ST_DWithin(
--     semantic_position, 
--     (SELECT semantic_position FROM atom WHERE codepoint = 97), 
--     100
-- );

-- Get all children of a composition:
-- SELECT a.* 
-- FROM composition_relation cr
-- JOIN atom a ON a.hilbert_high = cr.child_hilbert_high 
--            AND a.hilbert_low = cr.child_hilbert_low
-- WHERE cr.parent_hilbert_high = ? AND cr.parent_hilbert_low = ?
-- ORDER BY cr.child_index;

-- Recursive traversal to extract all leaf atoms from a composition:
-- WITH RECURSIVE tree AS (
--     SELECT hilbert_high, hilbert_low, codepoint, 0 AS depth 
--     FROM atom 
--     WHERE hilbert_high = ? AND hilbert_low = ?
--     
--     UNION ALL
--     
--     SELECT a.hilbert_high, a.hilbert_low, a.codepoint, t.depth + 1
--     FROM tree t
--     JOIN composition_relation cr 
--         ON cr.parent_hilbert_high = t.hilbert_high 
--        AND cr.parent_hilbert_low = t.hilbert_low
--     JOIN atom a 
--         ON a.hilbert_high = cr.child_hilbert_high 
--        AND a.hilbert_low = cr.child_hilbert_low
-- )
-- SELECT * FROM tree WHERE codepoint IS NOT NULL ORDER BY depth;
