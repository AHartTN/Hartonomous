-- ============================================================================
-- Text Search Extensions Configuration
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- ============================================================================

-- pg_trgm: Trigram matching for text similarity
-- Used for Levenshtein distance and fuzzy text matching in spatial positioning
CREATE EXTENSION IF NOT EXISTS pg_trgm;

COMMENT ON EXTENSION pg_trgm IS 
'Text similarity measurement and index searching based on trigrams';
