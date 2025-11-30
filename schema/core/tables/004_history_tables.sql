-- ============================================================================
-- TEMPORAL HISTORY TABLES
-- Complete audit trail for all three core tables
-- ============================================================================

-- Atom history (temporal versioning)
CREATE TABLE IF NOT EXISTS atom_history (
    history_id BIGSERIAL PRIMARY KEY,
    atom_id BIGINT NOT NULL,
    content_hash BYTEA NOT NULL,
    atom_value BYTEA CHECK (length(atom_value) <= 64),
    canonical_text TEXT,
    spatial_key GEOMETRY(POINTZM, 0),
    reference_count BIGINT NOT NULL DEFAULT 1,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_atom_history_atom_id ON atom_history(atom_id);
CREATE INDEX IF NOT EXISTS idx_atom_history_valid_range ON atom_history(atom_id, valid_from, valid_to);

COMMENT ON TABLE atom_history IS 'Historical versions of atoms for temporal queries and audit trail';

-- AtomComposition history
CREATE TABLE IF NOT EXISTS atom_composition_history (
    history_id BIGSERIAL PRIMARY KEY,
    composition_id BIGINT NOT NULL,
    parent_atom_id BIGINT NOT NULL,
    component_atom_id BIGINT NOT NULL,
    sequence_index BIGINT NOT NULL,
    spatial_key GEOMETRY(POINTZM, 0),
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_atom_composition_history_composition_id ON atom_composition_history(composition_id);
CREATE INDEX IF NOT EXISTS idx_atom_composition_history_valid_range ON atom_composition_history(composition_id, valid_from, valid_to);

COMMENT ON TABLE atom_composition_history IS 'Historical versions of compositions for temporal queries';

-- AtomRelation history
CREATE TABLE IF NOT EXISTS atom_relation_history (
    history_id BIGSERIAL PRIMARY KEY,
    relation_id BIGINT NOT NULL,
    source_atom_id BIGINT NOT NULL,
    target_atom_id BIGINT NOT NULL,
    relation_type_id BIGINT NOT NULL,
    weight REAL NOT NULL DEFAULT 0.5,
    confidence REAL NOT NULL DEFAULT 0.5,
    importance REAL NOT NULL DEFAULT 0.5,
    spatial_expression GEOMETRY(LINESTRINGZ, 0),
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    last_accessed TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_atom_relation_history_relation_id ON atom_relation_history(relation_id);
CREATE INDEX IF NOT EXISTS idx_atom_relation_history_valid_range ON atom_relation_history(relation_id, valid_from, valid_to);

COMMENT ON TABLE atom_relation_history IS 'Historical versions of relations for temporal queries';
