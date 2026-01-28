-- ==============================================================================
-- Hartonomous Core Tables
-- ==============================================================================
-- This file is IDEMPOTENT - safe to run multiple times
-- Creates atoms, compositions, and relations tables

SET search_path TO hartonomous, public;

-- ==============================================================================
-- ATOMS: Unicode codepoints
-- ==============================================================================

CREATE TABLE IF NOT EXISTS atoms (
    hash BYTEA PRIMARY KEY,
    codepoint INTEGER NOT NULL UNIQUE,

    -- 4D position on S³ sphere (individual coordinates)
    centroid_x DOUBLE PRECISION NOT NULL,
    centroid_y DOUBLE PRECISION NOT NULL,
    centroid_z DOUBLE PRECISION NOT NULL,
    centroid_w DOUBLE PRECISION NOT NULL,

    -- PostGIS 4D geometry (SRID 0 for abstract space)
    centroid GEOMETRY(POINTZM, 0) NOT NULL,

    -- Hilbert curve index for spatial ordering
    hilbert_hi BIGINT NOT NULL,
    hilbert_lo BIGINT NOT NULL,

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

    -- Ensure centroid is on S³ (unit sphere)
    CONSTRAINT atoms_centroid_normalized CHECK (
        ABS(centroid_x * centroid_x +
            centroid_y * centroid_y +
            centroid_z * centroid_z +
            centroid_w * centroid_w - 1.0) < 0.0001
    )
);

-- Spatial index using GIST
CREATE INDEX IF NOT EXISTS idx_atoms_centroid ON atoms USING GIST(centroid);
CREATE INDEX IF NOT EXISTS idx_atoms_hilbert ON atoms(hilbert_hi, hilbert_lo);
CREATE INDEX IF NOT EXISTS idx_atoms_codepoint ON atoms(codepoint);

-- ==============================================================================
-- COMPOSITIONS: Words, phrases, embeddings
-- ==============================================================================

CREATE TABLE IF NOT EXISTS compositions (
    hash BYTEA PRIMARY KEY,
    text TEXT NOT NULL,

    -- 4D centroid on S³ (individual coordinates)
    centroid_x DOUBLE PRECISION NOT NULL,
    centroid_y DOUBLE PRECISION NOT NULL,
    centroid_z DOUBLE PRECISION NOT NULL,
    centroid_w DOUBLE PRECISION NOT NULL,

    -- PostGIS 4D geometry (SRID 0 for abstract space)
    centroid GEOMETRY(POINTZM, 0) NOT NULL,

    -- 4D path through atoms (LINESTRINGZM for trajectory)
    path GEOMETRY(LINESTRINGZM, 0),

    -- Hilbert curve index
    hilbert_hi BIGINT NOT NULL,
    hilbert_lo BIGINT NOT NULL,

    -- Metadata
    length INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    access_count BIGINT DEFAULT 0,

    -- Ensure centroid is on S³
    CONSTRAINT compositions_centroid_normalized CHECK (
        ABS(centroid_x * centroid_x +
            centroid_y * centroid_y +
            centroid_z * centroid_z +
            centroid_w * centroid_w - 1.0) < 0.0001
    )
);

-- Spatial indexes
CREATE INDEX IF NOT EXISTS idx_compositions_centroid ON compositions USING GIST(centroid);
CREATE INDEX IF NOT EXISTS idx_compositions_path ON compositions USING GIST(path);
CREATE INDEX IF NOT EXISTS idx_compositions_hilbert ON compositions(hilbert_hi, hilbert_lo);
CREATE INDEX IF NOT EXISTS idx_compositions_text ON compositions USING gin(to_tsvector('english', text));
CREATE INDEX IF NOT EXISTS idx_compositions_hash ON compositions(hash);

-- ==============================================================================
-- COMPOSITION_ATOMS: Many-to-many relationship
-- ==============================================================================

CREATE TABLE IF NOT EXISTS composition_atoms (
    composition_hash BYTEA NOT NULL REFERENCES compositions(hash) ON DELETE CASCADE,
    atom_hash BYTEA NOT NULL REFERENCES atoms(hash) ON DELETE CASCADE,
    position INTEGER NOT NULL,

    PRIMARY KEY (composition_hash, atom_hash, position)
);

CREATE INDEX IF NOT EXISTS idx_composition_atoms_composition ON composition_atoms(composition_hash);
CREATE INDEX IF NOT EXISTS idx_composition_atoms_atom ON composition_atoms(atom_hash);

-- ==============================================================================
-- RELATIONS: Sentences, paragraphs, context
-- ==============================================================================

CREATE TABLE IF NOT EXISTS relations (
    hash BYTEA PRIMARY KEY,
    level INTEGER NOT NULL DEFAULT 1,
    length INTEGER NOT NULL DEFAULT 0,

    -- 4D centroid on S³ (individual coordinates)
    centroid_x DOUBLE PRECISION NOT NULL,
    centroid_y DOUBLE PRECISION NOT NULL,
    centroid_z DOUBLE PRECISION NOT NULL,
    centroid_w DOUBLE PRECISION NOT NULL,

    -- PostGIS 4D geometry (SRID 0 for abstract space)
    centroid GEOMETRY(POINTZM, 0) NOT NULL,

    -- Hilbert curve index (128-bit split)
    hilbert_hi BIGINT NOT NULL,
    hilbert_lo BIGINT NOT NULL,

    -- 4D path through children
    path GEOMETRY(LINESTRINGZM, 0),

    -- Parent type: 'composition' or 'relation'
    parent_type TEXT NOT NULL CHECK (parent_type IN ('composition', 'relation')),

    -- Metadata
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

    -- Ensure centroid is on S³
    CONSTRAINT relations_centroid_normalized CHECK (
        ABS(centroid_x * centroid_x +
            centroid_y * centroid_y +
            centroid_z * centroid_z +
            centroid_w * centroid_w - 1.0) < 0.0001
    )
);

-- Spatial indexes
CREATE INDEX IF NOT EXISTS idx_relations_centroid ON relations USING GIST(centroid);
CREATE INDEX IF NOT EXISTS idx_relations_path ON relations USING GIST(path);
CREATE INDEX IF NOT EXISTS idx_relations_level ON relations(level);
CREATE INDEX IF NOT EXISTS idx_relations_hilbert ON relations(hilbert_hi, hilbert_lo);
CREATE INDEX IF NOT EXISTS idx_relations_hash ON relations(hash);

-- ==============================================================================
-- RELATION_CHILDREN: Compositions or relations that make up this relation
-- ==============================================================================

CREATE TABLE IF NOT EXISTS relation_children (
    relation_hash BYTEA NOT NULL REFERENCES relations(hash) ON DELETE CASCADE,
    child_hash BYTEA NOT NULL,
    child_type TEXT NOT NULL CHECK (child_type IN ('composition', 'relation')),
    position INTEGER NOT NULL,

    PRIMARY KEY (relation_hash, child_hash, position)
);

CREATE INDEX IF NOT EXISTS idx_relation_children_relation ON relation_children(relation_hash);
CREATE INDEX IF NOT EXISTS idx_relation_children_child ON relation_children(child_hash);
CREATE INDEX IF NOT EXISTS idx_relation_children_type ON relation_children(child_type);

-- ==============================================================================
-- METADATA: Key-value store for embeddings and other metadata
-- ==============================================================================

CREATE TABLE IF NOT EXISTS metadata (
    hash BYTEA NOT NULL,
    entity_type TEXT NOT NULL CHECK (entity_type IN ('atom', 'composition', 'relation')),
    key TEXT NOT NULL,
    value JSONB NOT NULL,

    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (hash, entity_type, key)
);

CREATE INDEX IF NOT EXISTS idx_metadata_hash ON metadata(hash);
CREATE INDEX IF NOT EXISTS idx_metadata_key ON metadata(key);
CREATE INDEX IF NOT EXISTS idx_metadata_value ON metadata USING gin(value);

-- Record schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (2, 'Core tables: atoms, compositions, relations')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;

-- Success message
DO $$
BEGIN
    RAISE NOTICE 'Core tables installed successfully';
    RAISE NOTICE '  - atoms: % rows', (SELECT COUNT(*) FROM atoms);
    RAISE NOTICE '  - compositions: % rows', (SELECT COUNT(*) FROM compositions);
    RAISE NOTICE '  - relations: % rows', (SELECT COUNT(*) FROM relations);
END $$;
