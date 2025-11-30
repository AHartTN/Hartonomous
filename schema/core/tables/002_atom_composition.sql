-- ============================================================================
-- ATOM_COMPOSITION TABLE (DEPRECATED - LEGACY)
-- ============================================================================
-- 
-- **NOTE**: This table is now DEPRECATED in favor of fractal composition.
-- 
-- NEW APPROACH: Compositions are stored in the atom table itself via 
-- composition_ids BIGINT[] column. This enables:
--   - O(1) deduplication via coordinate collision
--   - Fractal compression (reuse atoms across documents)
--   - Simpler schema (one table instead of two)
-- 
-- This table remains for:
--   - Historical data migration
--   - Specialized use cases requiring explicit parent-child tracking
--   - Backward compatibility
-- 
-- For new code, use FractalAtomizer and atom.composition_ids instead.
-- ============================================================================

CREATE TABLE IF NOT EXISTS atom_composition (
    -- Identity
    composition_id BIGSERIAL PRIMARY KEY,
    
    -- Parent-child relationship
    parent_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    component_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    
    -- Order matters - sequence within parent
    sequence_index BIGINT NOT NULL,
    
    -- Local coordinate frame (position relative to parent) with Hilbert index
    spatial_key GEOMETRY(POINTZM, 0),
    
    -- Flexible metadata
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    
    -- Temporal versioning
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz,
    
    -- Uniqueness constraint (parent can't have duplicate component at same index)
    UNIQUE (parent_atom_id, component_atom_id, sequence_index)
);

-- ============================================================================
-- COMMENTS
-- ============================================================================

COMMENT ON TABLE atom_composition IS '[DEPRECATED] Use atom.composition_ids instead. Legacy table for explicit parent-child tracking.';
COMMENT ON COLUMN atom_composition.parent_atom_id IS 'The containing/parent atom (e.g., document, sentence, vector)';
COMMENT ON COLUMN atom_composition.component_atom_id IS 'The contained/child atom (e.g., word, character, float value)';
COMMENT ON COLUMN atom_composition.sequence_index IS 'Position within parent. Gaps = implicit zeros (sparse representation)';
COMMENT ON COLUMN atom_composition.spatial_key IS 'Local position relative to parent coordinate frame';
