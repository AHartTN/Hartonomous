-- ============================================================================
-- ATOM_COMPOSITION TABLE
-- Hierarchical structure - documents ? sentences ? words ? characters
-- ============================================================================

CREATE TABLE IF NOT EXISTS atom_composition (
    -- Identity
    composition_id BIGSERIAL PRIMARY KEY,
    
    -- Parent-child relationship
    parent_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    component_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    
    -- Order matters - sequence within parent
    sequence_index BIGINT NOT NULL,
    
    -- Local coordinate frame (position relative to parent)
    spatial_key GEOMETRY(POINTZ, 0),
    
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

COMMENT ON TABLE atom_composition IS 'Hierarchical composition: defines structure (what contains what, in what order). Sparse by default - missing sequence_index = implicit zero.';
COMMENT ON COLUMN atom_composition.parent_atom_id IS 'The containing/parent atom (e.g., document, sentence, vector)';
COMMENT ON COLUMN atom_composition.component_atom_id IS 'The contained/child atom (e.g., word, character, float value)';
COMMENT ON COLUMN atom_composition.sequence_index IS 'Position within parent. Gaps = implicit zeros (sparse representation)';
COMMENT ON COLUMN atom_composition.spatial_key IS 'Local position relative to parent coordinate frame';
