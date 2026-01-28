-- ==============================================================================
-- Hartonomous Foundation Schema
-- ==============================================================================
-- This file is IDEMPOTENT - safe to run multiple times
-- Creates core extensions and types

-- Enable PostGIS for spatial queries
CREATE EXTENSION IF NOT EXISTS postgis;

-- Enable pgcrypto for hashing functions
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Create custom types (idempotent)

-- 4D point on S³ sphere
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 's3_point') THEN
        CREATE TYPE s3_point AS (
            x DOUBLE PRECISION,
            y DOUBLE PRECISION,
            z DOUBLE PRECISION,
            w DOUBLE PRECISION
        );
    END IF;
END $$;

-- Ingestion statistics
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'ingestion_stats') THEN
        CREATE TYPE ingestion_stats AS (
            atoms_new BIGINT,
            atoms_existing BIGINT,
            compositions_new BIGINT,
            compositions_existing BIGINT,
            relations_total BIGINT,
            original_bytes BIGINT,
            stored_bytes BIGINT,
            compression_ratio DOUBLE PRECISION
        );
    END IF;
END $$;

-- Query result
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'query_result') THEN
        CREATE TYPE query_result AS (
            text TEXT,
            confidence DOUBLE PRECISION
        );
    END IF;
END $$;

-- Create schemas for organization
CREATE SCHEMA IF NOT EXISTS hartonomous;
CREATE SCHEMA IF NOT EXISTS hartonomous_internal;

-- Set search path
SET search_path TO hartonomous, public;

-- Version tracking
CREATE TABLE IF NOT EXISTS hartonomous_internal.schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    description TEXT
);

-- Record this schema version
INSERT INTO hartonomous_internal.schema_version (version, description)
VALUES (1, 'Foundation schema with PostGIS and custom types')
ON CONFLICT (version) DO UPDATE SET applied_at = CURRENT_TIMESTAMP;

-- Success message
DO $$
BEGIN
    RAISE NOTICE 'Foundation schema installed successfully';
END $$;
