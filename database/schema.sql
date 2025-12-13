-- Hartonomous Database Schema
-- PostgreSQL 16+ with PostGIS 3.4+
-- Production-grade spatial AI architecture

-- Extensions
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_topology;
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

-- Drop existing objects for idempotent deployment
DROP TABLE IF EXISTS atom CASCADE;
DROP TABLE IF EXISTS atom_compositions CASCADE;
DROP TABLE IF EXISTS atom_embeddings CASCADE;
DROP TABLE IF EXISTS cortex_state CASCADE;
DROP TABLE IF EXISTS cortex_landmarks CASCADE;

-- Core table: Atoms in 4D semantic space
CREATE TABLE atom (
    -- Structured Deterministic Identity (BLAKE3 hash)
    -- [Modality 8b][SemanticClass 16b][Normalization 32b][ValueSig 200b]
    atom_id BYTEA NOT NULL,
    
    -- Binary ontology: 0=Constant, 1=Composition
    atom_class SMALLINT NOT NULL CHECK (atom_class IN (0, 1)),
    
    -- Type classification
    modality SMALLINT NOT NULL,
    subtype VARCHAR(50),
    
    -- Raw value for Constants only (max 64 bytes)
    atomic_value BYTEA CHECK (atomic_value IS NULL OR octet_length(atomic_value) <= 64),
    
    -- 4D semantic manifold (XYZM)
    -- X,Y: Learned coordinates via LMDS
    -- Z: Hierarchy (0=raw, 1=feature, 2=concept, 3=abstraction)
    -- M: Salience/frequency
    geom GEOMETRY(POINTZM, 4326) NOT NULL,
    
    -- Hilbert index for physical clustering
    hilbert_index BIGINT NOT NULL,
    
    -- Metadata (JSON for flexibility)
    metadata JSONB,
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    
    -- Constraints
    CONSTRAINT pk_atoms PRIMARY KEY (atom_id),
    CONSTRAINT chk_constant_has_value CHECK (
        (atom_class = 0 AND atomic_value IS NOT NULL) OR
        (atom_class = 1 AND atomic_value IS NULL)
    )
);

-- Spatial index (GiST R-Tree) for O(log N) k-NN queries
CREATE INDEX idx_atoms_geom_gist ON atom USING GIST (geom);

-- B-Tree indexes for lookups
CREATE INDEX idx_atoms_class ON atom (atom_class);
CREATE INDEX idx_atoms_modality ON atom (modality);
CREATE INDEX idx_atoms_hilbert ON atom (hilbert_index);
CREATE INDEX idx_atoms_created ON atom (created_at);

-- GIN index for metadata queries
CREATE INDEX idx_atoms_metadata ON atom USING GIN (metadata);

-- Composition relationships table
CREATE TABLE atom_compositions (
    parent_atom_id BYTEA NOT NULL,
    component_atom_id BYTEA NOT NULL,
    sequence_index INTEGER NOT NULL,
    
    -- Constraints
    CONSTRAINT pk_compositions PRIMARY KEY (parent_atom_id, component_atom_id, sequence_index),
    CONSTRAINT fk_parent FOREIGN KEY (parent_atom_id) 
        REFERENCES atom(atom_id) ON DELETE CASCADE,
    CONSTRAINT fk_component FOREIGN KEY (component_atom_id) 
        REFERENCES atom(atom_id) ON DELETE CASCADE
);

CREATE INDEX idx_compositions_parent ON atom_compositions (parent_atom_id);
CREATE INDEX idx_compositions_component ON atom_compositions (component_atom_id);

-- LMDS landmark atoms for Cortex
CREATE TABLE cortex_landmarks (
    atom_id BYTEA NOT NULL,
    landmark_index INTEGER NOT NULL,
    model_version INTEGER NOT NULL DEFAULT 1,
    selected_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    
    CONSTRAINT pk_landmarks PRIMARY KEY (atom_id),
    CONSTRAINT fk_landmark_atom FOREIGN KEY (atom_id) 
        REFERENCES atom(atom_id) ON DELETE CASCADE
);

CREATE INDEX idx_landmarks_version ON cortex_landmarks (model_version);

-- Cortex state tracking
CREATE TABLE cortex_state (
    id INTEGER PRIMARY KEY DEFAULT 1,
    model_version INTEGER NOT NULL DEFAULT 1,
    atoms_processed BIGINT NOT NULL DEFAULT 0,
    recalibrations BIGINT NOT NULL DEFAULT 0,
    avg_stress DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    last_cycle_at TIMESTAMPTZ,
    landmark_count INTEGER NOT NULL DEFAULT 0,
    
    CONSTRAINT chk_singleton CHECK (id = 1)
);

INSERT INTO cortex_state (id) VALUES (1);

-- Updated timestamp trigger
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_atoms_updated_at
    BEFORE UPDATE ON atom
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- PostgreSQL tuning for spatial workload
ALTER SYSTEM SET shared_buffers = '4GB';
ALTER SYSTEM SET effective_cache_size = '12GB';
ALTER SYSTEM SET maintenance_work_mem = '1GB';
ALTER SYSTEM SET random_page_cost = 1.1;  -- NVMe SSD
ALTER SYSTEM SET effective_io_concurrency = 200;
ALTER SYSTEM SET max_parallel_workers_per_gather = 4;
ALTER SYSTEM SET max_parallel_workers = 8;
ALTER SYSTEM SET max_worker_processes = 8;

-- Reload configuration
SELECT pg_reload_conf();

-- Grant permissions (idempotent)
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'hartonomous_app') THEN
        CREATE ROLE hartonomous_app WITH LOGIN PASSWORD 'CHANGE_IN_PRODUCTION';
    END IF;
END
$$;
GRANT CONNECT ON DATABASE hartonomous TO hartonomous_app;
GRANT SELECT, INSERT, UPDATE ON atom TO hartonomous_app;
GRANT SELECT, INSERT ON atom_compositions TO hartonomous_app;
GRANT SELECT ON cortex_landmarks TO hartonomous_app;
GRANT SELECT ON cortex_state TO hartonomous_app;
