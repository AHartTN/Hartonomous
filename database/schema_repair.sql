-- Hartonomous Database Repair Schema
-- Idempotent - handles schema drift, column changes, missing objects
-- Safe to run on existing databases - preserves data

-- Extensions (idempotent)
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_topology;
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

-- Core table: atom (create or alter to match expected schema)
CREATE TABLE IF NOT EXISTS atom (
    atom_id BYTEA NOT NULL,
    atom_class SMALLINT NOT NULL,
    modality SMALLINT NOT NULL,
    subtype VARCHAR(50),
    atomic_value BYTEA,
    geom GEOMETRY(POINTZM, 0) NOT NULL,
    hilbert_index BIGINT NOT NULL,
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Add missing columns to atom table (idempotent)
DO $$
BEGIN
    -- Add atom_class if missing
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'atom_class') THEN
        ALTER TABLE atom ADD COLUMN atom_class SMALLINT NOT NULL DEFAULT 0;
    END IF;
    
    -- Add modality if missing
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'modality') THEN
        ALTER TABLE atom ADD COLUMN modality SMALLINT NOT NULL DEFAULT 0;
    END IF;
    
    -- Add subtype if missing
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'subtype') THEN
        ALTER TABLE atom ADD COLUMN subtype VARCHAR(50);
    END IF;
    
    -- Add atomic_value if missing
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'atomic_value') THEN
        ALTER TABLE atom ADD COLUMN atomic_value BYTEA;
    END IF;
    
    -- Add geom if missing (critical)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'geom') THEN
        ALTER TABLE atom ADD COLUMN geom GEOMETRY(POINTZM, 0);
        -- Set default for existing rows
        UPDATE atom SET geom = ST_MakePoint(0, 0, 0, 0) WHERE geom IS NULL;
        ALTER TABLE atom ALTER COLUMN geom SET NOT NULL;
    END IF;
    
    -- Add hilbert_index if missing
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'hilbert_index') THEN
        ALTER TABLE atom ADD COLUMN hilbert_index BIGINT NOT NULL DEFAULT 0;
    END IF;
    
    -- Add metadata if missing
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'metadata') THEN
        ALTER TABLE atom ADD COLUMN metadata JSONB;
    END IF;
    
    -- Add created_at if missing
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'created_at') THEN
        ALTER TABLE atom ADD COLUMN created_at TIMESTAMPTZ NOT NULL DEFAULT now();
    END IF;
    
    -- Add updated_at if missing
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_schema = 'public' AND table_name = 'atom' AND column_name = 'updated_at') THEN
        ALTER TABLE atom ADD COLUMN updated_at TIMESTAMPTZ NOT NULL DEFAULT now();
    END IF;
END $$;

-- Add constraints if missing (idempotent)
DO $$
BEGIN
    -- Primary key
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'pk_atoms' AND conrelid = 'atom'::regclass) THEN
        ALTER TABLE atom ADD CONSTRAINT pk_atoms PRIMARY KEY (atom_id);
    END IF;
    
    -- Check constraint: atom_class values
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'atom_atom_class_check' AND conrelid = 'atom'::regclass) THEN
        ALTER TABLE atom ADD CONSTRAINT atom_atom_class_check CHECK (atom_class IN (0, 1));
    END IF;
    
    -- Check constraint: atomic_value size
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'atom_atomic_value_check' AND conrelid = 'atom'::regclass) THEN
        ALTER TABLE atom ADD CONSTRAINT atom_atomic_value_check 
            CHECK (atomic_value IS NULL OR octet_length(atomic_value) <= 64);
    END IF;
    
    -- Check constraint: constant has value
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_constant_has_value' AND conrelid = 'atom'::regclass) THEN
        ALTER TABLE atom ADD CONSTRAINT chk_constant_has_value 
            CHECK ((atom_class = 0 AND atomic_value IS NOT NULL) OR (atom_class = 1 AND atomic_value IS NULL));
    END IF;
END $$;

-- Indexes (idempotent with concurrency)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_geom_gist') THEN
        CREATE INDEX CONCURRENTLY idx_atoms_geom_gist ON atom USING GIST (geom);
    END IF;
EXCEPTION
    WHEN OTHERS THEN
        -- Non-concurrent fallback for databases without concurrent index support
        IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_geom_gist') THEN
            CREATE INDEX idx_atoms_geom_gist ON atom USING GIST (geom);
        END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_class') THEN
        CREATE INDEX CONCURRENTLY idx_atoms_class ON atom (atom_class);
    END IF;
EXCEPTION WHEN OTHERS THEN
        IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_class') THEN
            CREATE INDEX idx_atoms_class ON atom (atom_class);
        END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_modality') THEN
        CREATE INDEX CONCURRENTLY idx_atoms_modality ON atom (modality);
    END IF;
EXCEPTION WHEN OTHERS THEN
        IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_modality') THEN
            CREATE INDEX idx_atoms_modality ON atom (modality);
        END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_hilbert') THEN
        CREATE INDEX CONCURRENTLY idx_atoms_hilbert ON atom (hilbert_index);
    END IF;
EXCEPTION WHEN OTHERS THEN
        IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_hilbert') THEN
            CREATE INDEX idx_atoms_hilbert ON atom (hilbert_index);
        END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_created') THEN
        CREATE INDEX CONCURRENTLY idx_atoms_created ON atom (created_at);
    END IF;
EXCEPTION WHEN OTHERS THEN
        IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_created') THEN
            CREATE INDEX idx_atoms_created ON atom (created_at);
        END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_metadata') THEN
        CREATE INDEX CONCURRENTLY idx_atoms_metadata ON atom USING GIN (metadata);
    END IF;
EXCEPTION WHEN OTHERS THEN
        IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atoms_metadata') THEN
            CREATE INDEX idx_atoms_metadata ON atom USING GIN (metadata);
        END IF;
END $$;

-- Composition relationships table
CREATE TABLE IF NOT EXISTS atom_compositions (
    parent_atom_id BYTEA NOT NULL,
    component_atom_id BYTEA NOT NULL,
    sequence_index INTEGER NOT NULL
);

-- Add constraints to atom_compositions
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'pk_compositions' AND conrelid = 'atom_compositions'::regclass) THEN
        ALTER TABLE atom_compositions ADD CONSTRAINT pk_compositions 
            PRIMARY KEY (parent_atom_id, component_atom_id, sequence_index);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_parent' AND conrelid = 'atom_compositions'::regclass) THEN
        ALTER TABLE atom_compositions ADD CONSTRAINT fk_parent 
            FOREIGN KEY (parent_atom_id) REFERENCES atom(atom_id) ON DELETE CASCADE;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_component' AND conrelid = 'atom_compositions'::regclass) THEN
        ALTER TABLE atom_compositions ADD CONSTRAINT fk_component 
            FOREIGN KEY (component_atom_id) REFERENCES atom(atom_id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_compositions_parent') THEN
        CREATE INDEX idx_compositions_parent ON atom_compositions (parent_atom_id);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_compositions_component') THEN
        CREATE INDEX idx_compositions_component ON atom_compositions (component_atom_id);
    END IF;
END $$;

-- Cortex landmarks table
CREATE TABLE IF NOT EXISTS cortex_landmarks (
    atom_id BYTEA NOT NULL,
    landmark_index INTEGER NOT NULL,
    model_version INTEGER NOT NULL DEFAULT 1,
    selected_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'pk_landmarks' AND conrelid = 'cortex_landmarks'::regclass) THEN
        ALTER TABLE cortex_landmarks ADD CONSTRAINT pk_landmarks PRIMARY KEY (atom_id);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_landmark_atom' AND conrelid = 'cortex_landmarks'::regclass) THEN
        ALTER TABLE cortex_landmarks ADD CONSTRAINT fk_landmark_atom 
            FOREIGN KEY (atom_id) REFERENCES atom(atom_id) ON DELETE CASCADE;
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_landmarks_version') THEN
        CREATE INDEX idx_landmarks_version ON cortex_landmarks (model_version);
    END IF;
END $$;

-- Cortex state table
CREATE TABLE IF NOT EXISTS cortex_state (
    id INTEGER PRIMARY KEY DEFAULT 1,
    model_version INTEGER NOT NULL DEFAULT 1,
    atoms_processed BIGINT NOT NULL DEFAULT 0,
    recalibrations BIGINT NOT NULL DEFAULT 0,
    avg_stress DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    last_cycle_at TIMESTAMPTZ
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'cortex_state_single_row' AND conrelid = 'cortex_state'::regclass) THEN
        ALTER TABLE cortex_state ADD CONSTRAINT cortex_state_single_row CHECK (id = 1);
    END IF;
END $$;

-- Initialize cortex_state
INSERT INTO cortex_state (id, model_version, atoms_processed, recalibrations, avg_stress)
VALUES (1, 1, 0, 0, 0.0)
ON CONFLICT (id) DO NOTHING;

-- Updated_at trigger function
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Trigger (idempotent)
DROP TRIGGER IF EXISTS trg_atoms_updated_at ON atom;
CREATE TRIGGER trg_atoms_updated_at
    BEFORE UPDATE ON atom
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Permissions (idempotent)
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'hartonomous_app') THEN
        CREATE ROLE hartonomous_app WITH LOGIN PASSWORD 'CHANGE_IN_PRODUCTION';
    END IF;
END $$;

GRANT CONNECT ON DATABASE hartonomous TO hartonomous_app;
GRANT SELECT, INSERT, UPDATE ON atom TO hartonomous_app;
GRANT SELECT, INSERT ON atom_compositions TO hartonomous_app;
GRANT SELECT ON cortex_landmarks TO hartonomous_app;
GRANT SELECT ON cortex_state TO hartonomous_app;

-- Analyze tables for query planner
ANALYZE atom;
ANALYZE atom_compositions;
ANALYZE cortex_landmarks;
ANALYZE cortex_state;
