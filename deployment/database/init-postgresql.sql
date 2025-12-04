-- Hartonomous PostgreSQL Database Initialization Script
-- Run as postgres superuser

-- Create database
CREATE DATABASE hartonomous
    WITH
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.UTF-8'
    LC_CTYPE = 'en_US.UTF-8'
    TEMPLATE = template0;

-- Connect to the new database
\c hartonomous;

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS btree_gin;
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- Create user and grant permissions
-- Note: Replace 'CHANGE_ME_STRONG_PASSWORD' with actual password
DO
$$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'hartonomous_user') THEN
        CREATE USER hartonomous_user WITH PASSWORD 'CHANGE_ME_STRONG_PASSWORD';
    END IF;
END
$$;

-- Grant permissions
GRANT ALL PRIVILEGES ON DATABASE hartonomous TO hartonomous_user;
GRANT ALL ON SCHEMA public TO hartonomous_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO hartonomous_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO hartonomous_user;

-- Create schemas for organization
CREATE SCHEMA IF NOT EXISTS cas;  -- Content-Addressable Storage
CREATE SCHEMA IF NOT EXISTS bpe;  -- Byte-Pair Encoding
CREATE SCHEMA IF NOT EXISTS meta; -- Metadata

GRANT ALL ON SCHEMA cas TO hartonomous_user;
GRANT ALL ON SCHEMA bpe TO hartonomous_user;
GRANT ALL ON SCHEMA meta TO hartonomous_user;

-- Set search path
ALTER DATABASE hartonomous SET search_path TO public, cas, bpe, meta;

-- Content-Addressable Storage Tables
CREATE TABLE IF NOT EXISTS cas.atoms (
    id BIGSERIAL PRIMARY KEY,
    content_hash VARCHAR(64) UNIQUE NOT NULL,
    content BYTEA NOT NULL,
    size_bytes INTEGER NOT NULL,
    hilbert_index BIGINT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    access_count INTEGER DEFAULT 0,
    last_accessed_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX IF NOT EXISTS idx_atoms_content_hash ON cas.atoms(content_hash);
CREATE INDEX IF NOT EXISTS idx_atoms_hilbert_index ON cas.atoms(hilbert_index);
CREATE INDEX IF NOT EXISTS idx_atoms_created_at ON cas.atoms(created_at DESC);

-- BPE Vocabulary Tables
CREATE TABLE IF NOT EXISTS bpe.vocabulary (
    id SERIAL PRIMARY KEY,
    token BYTEA UNIQUE NOT NULL,
    token_id INTEGER UNIQUE NOT NULL,
    frequency BIGINT DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_vocabulary_token_id ON bpe.vocabulary(token_id);
CREATE INDEX IF NOT EXISTS idx_vocabulary_frequency ON bpe.vocabulary(frequency DESC);

-- Metadata Tables
CREATE TABLE IF NOT EXISTS meta.files (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    filename VARCHAR(1024) NOT NULL,
    original_size BIGINT NOT NULL,
    compressed_size BIGINT,
    content_type VARCHAR(255),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    metadata JSONB
);

CREATE INDEX IF NOT EXISTS idx_files_filename ON meta.files(filename);
CREATE INDEX IF NOT EXISTS idx_files_created_at ON meta.files(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_files_metadata ON meta.files USING gin(metadata);

-- File to Atom mapping (many-to-many)
CREATE TABLE IF NOT EXISTS meta.file_atoms (
    file_id UUID REFERENCES meta.files(id) ON DELETE CASCADE,
    atom_id BIGINT REFERENCES cas.atoms(id) ON DELETE CASCADE,
    sequence_order INTEGER NOT NULL,
    PRIMARY KEY (file_id, atom_id, sequence_order)
);

CREATE INDEX IF NOT EXISTS idx_file_atoms_file_id ON meta.file_atoms(file_id);
CREATE INDEX IF NOT EXISTS idx_file_atoms_atom_id ON meta.file_atoms(atom_id);

COMMENT ON DATABASE hartonomous IS 'Hartonomous Atomic Content-Addressable Storage System';
COMMENT ON SCHEMA cas IS 'Content-Addressable Storage - atomic chunks and deduplication';
COMMENT ON SCHEMA bpe IS 'Byte-Pair Encoding - vocabulary and tokenization';
COMMENT ON SCHEMA meta IS 'Metadata and file management';

-- Success message
DO $$
BEGIN
    RAISE NOTICE 'Hartonomous database initialized successfully!';
    RAISE NOTICE 'Database: hartonomous';
    RAISE NOTICE 'User: hartonomous_user';
    RAISE NOTICE 'Schemas: public, cas, bpe, meta';
END $$;
