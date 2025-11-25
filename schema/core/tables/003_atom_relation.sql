-- ============================================================================
-- ATOM_RELATION TABLE
-- Semantic graph - synaptic connections, learned relationships
-- ============================================================================

CREATE TABLE IF NOT EXISTS atom_relation (
    -- Identity
    relation_id BIGSERIAL PRIMARY KEY,
    
    -- Source and target atoms
    source_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    target_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    
    -- Relation type is itself an atom!
    relation_type_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE RESTRICT,
    
    -- Synaptic weights (Hebbian learning)
    weight REAL NOT NULL DEFAULT 0.5 CHECK (weight >= 0.0 AND weight <= 1.0),
    confidence REAL NOT NULL DEFAULT 0.5 CHECK (confidence >= 0.0 AND confidence <= 1.0),
    importance REAL NOT NULL DEFAULT 0.5 CHECK (importance >= 0.0 AND importance <= 1.0),
    
    -- Geometric path through semantic space
    spatial_expression GEOMETRY(LINESTRINGZ, 0),
    
    -- Flexible metadata
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    
    -- Tracking
    last_accessed TIMESTAMPTZ NOT NULL DEFAULT now(),
    
    -- Temporal versioning
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz,
    
    -- Uniqueness: one relation of each type between same atoms
    UNIQUE (source_atom_id, target_atom_id, relation_type_id)
);

-- ============================================================================
-- COMMENTS
-- ============================================================================

COMMENT ON TABLE atom_relation IS 'Semantic graph: how atoms relate. Synaptic weights strengthen via Hebbian learning.';
COMMENT ON COLUMN atom_relation.source_atom_id IS 'The source/origin atom of the relation';
COMMENT ON COLUMN atom_relation.target_atom_id IS 'The target/destination atom of the relation';
COMMENT ON COLUMN atom_relation.relation_type_id IS 'Type of relation (itself an atom): semantic_similar, causes, produced_result, etc.';
COMMENT ON COLUMN atom_relation.weight IS 'Synaptic efficacy: strengthened by Hebbian learning (0.0 to 1.0)';
COMMENT ON COLUMN atom_relation.confidence IS 'Confidence in this relation (0.0 to 1.0)';
COMMENT ON COLUMN atom_relation.importance IS 'Importance/relevance score (0.0 to 1.0)';
COMMENT ON COLUMN atom_relation.spatial_expression IS 'Geometric path through semantic space (LINESTRING from source to target)';
COMMENT ON COLUMN atom_relation.last_accessed IS 'Last time this relation was traversed (for synaptic decay)';
