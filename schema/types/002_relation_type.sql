-- ============================================================================
-- Relation Type Enumeration
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- Common relation types for semantic graph
-- Extensible design: relation types are also stored as atoms
CREATE TYPE relation_type AS ENUM (
    'semantic_similar',     -- Semantic similarity
    'semantic_opposite',    -- Semantic opposition (antonyms)
    'is_a',                -- Type/subtype relationship
    'part_of',             -- Compositional relationship
    'synonym',             -- Synonyms
    'antonym',             -- Antonyms
    'causes',              -- Causal relationship
    'produced_by',         -- Generation/authorship
    'temporal_precedes',   -- Temporal ordering
    'spatial_near',        -- Spatial proximity
    'co_occurs',           -- Co-occurrence
    'semantic_correlation' -- General semantic correlation
);

COMMENT ON TYPE relation_type IS 
'Enumeration of common semantic relation types';
