-- Initialize PostgreSQL with required extensions for Hartonomous

-- PostGIS for spatial geometry operations
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_topology;

-- pgvector for vector similarity search
CREATE EXTENSION IF NOT EXISTS vector;

-- PL/Python for GPU compute functions
CREATE EXTENSION IF NOT EXISTS plpython3u;

-- UUID support
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Full-text search support
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Cryptographic functions
CREATE EXTENSION IF NOT EXISTS pgcrypto;

GRANT ALL PRIVILEGES ON DATABASE CURRENT_DATABASE() TO CURRENT_USER;

DO $$
BEGIN
    RAISE NOTICE 'PostgreSQL extensions initialized successfully';
    RAISE NOTICE '  - PostGIS: spatial geometry operations';
    RAISE NOTICE '  - pgvector: embedding similarity search';
    RAISE NOTICE '  - PL/Python3: GPU compute functions';
END $$;
