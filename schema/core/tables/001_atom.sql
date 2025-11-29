-- ============================================================================
-- ATOM TABLE
-- The periodic table of intelligence - every unique value ?64 bytes
-- ============================================================================

CREATE TABLE IF NOT EXISTS atom (
    -- Identity
    atom_id BIGSERIAL PRIMARY KEY,
    
    -- Content addressing (global deduplication via SHA-256)
    content_hash BYTEA UNIQUE NOT NULL,
    
    -- The actual atomic value (?64 bytes enforced)
    atomic_value BYTEA CHECK (length(atomic_value) <= 64),
    
    -- Cached text representation for text atoms
    canonical_text TEXT,
    
    -- Spatial semantics - 4D position in semantic space
    -- X, Y, Z: 3D semantic coordinates (position = meaning)
    -- M: Hilbert curve index (preserves locality for fast approximate search)
    spatial_key GEOMETRY(POINTZM, 0),
    
    -- Importance / atomic mass (how often referenced)
    reference_count BIGINT NOT NULL DEFAULT 1,
    
    -- Flexible metadata (modality, tenant, model_name, etc.)
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    
        -- Temporal versioning
        created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
        valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
        valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz
    );-- ============================================================================
-- COMMENTS
-- ============================================================================

COMMENT ON TABLE atom IS 'Content-addressable storage for all unique values ?64 bytes. Every piece of knowledge atomized to this table.';
COMMENT ON COLUMN atom.atom_id IS 'Unique identifier for this atom';
COMMENT ON COLUMN atom.content_hash IS 'SHA-256 hash of atomic_value - ensures global deduplication';
COMMENT ON COLUMN atom.atomic_value IS 'The actual value stored (?64 bytes). Larger values must be decomposed.';
COMMENT ON COLUMN atom.canonical_text IS 'Cached text representation for text atoms (performance optimization)';
COMMENT ON COLUMN atom.spatial_key IS '4D point in semantic space (X,Y,Z,M). Position = meaning. M = Hilbert index for fast approximate NN search.';
COMMENT ON COLUMN atom.reference_count IS 'Atomic mass - how many times this atom is referenced/composed';
COMMENT ON COLUMN atom.metadata IS 'Flexible JSONB for modality, model_name, tenant_id, confidence, etc.';
COMMENT ON COLUMN atom.valid_from IS 'Temporal versioning: when this version became valid';
COMMENT ON COLUMN atom.valid_to IS 'Temporal versioning: when this version was superseded';
