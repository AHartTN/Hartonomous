-- ============================================================================
-- Modality Enumeration Type
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- Modality types for atoms
-- Extensible design: add new modalities without schema changes via JSONB metadata
-- This enum provides type safety for known modalities
CREATE TYPE modality_type AS ENUM (
    'character',        -- Single character
    'word',            -- Single word
    'sentence',        -- Sentence
    'paragraph',       -- Paragraph
    'document',        -- Complete document
    'concept',         -- Abstract concept
    'image',           -- Image container
    'image_patch',     -- Image patch (16x16 pixels)
    'audio',           -- Audio container
    'phoneme',         -- Phoneme (audio atom)
    'video',           -- Video container
    'frame',           -- Video frame
    'embedding',       -- Vector embedding
    'model_output',    -- Model generated content
    'relation_type',   -- Relation type atom
    'unknown'          -- Default/unspecified
);

COMMENT ON TYPE modality_type IS 
'Enumeration of known data modalities. Extensible via metadata->modality for new types.';
