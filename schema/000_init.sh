#!/bin/bash
# ============================================================================
# Hartonomous Database Initialization
# Executes schema files in correct order
# ============================================================================

set -e

echo "============================================"
echo "Initializing Hartonomous Database"
echo "============================================"

# Enable required extensions
echo "Enabling extensions..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS postgis;
    CREATE EXTENSION IF NOT EXISTS plpython3u;
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    CREATE EXTENSION IF NOT EXISTS btree_gin;
EOSQL

echo "Extensions enabled: postgis, plpython3u, pg_trgm, btree_gin"
echo ""

# Execute table creation scripts
echo "Creating tables..."
for file in /docker-entrypoint-initdb.d/tables/*.sql; do
    if [ -f "$file" ]; then
        echo "  Executing: $(basename $file)"
        psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$file"
    fi
done
echo ""

# Execute index creation scripts
echo "Creating indexes..."
for file in /docker-entrypoint-initdb.d/indexes/*.sql; do
    if [ -f "$file" ]; then
        echo "  Executing: $(basename $file)"
        psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$file"
    fi
done
echo ""

# Execute trigger scripts
echo "Creating triggers..."
for file in /docker-entrypoint-initdb.d/triggers/*.sql; do
    if [ -f "$file" ]; then
        echo "  Executing: $(basename $file)"
        psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$file"
    fi
done
echo ""

# Execute function scripts
echo "Creating functions..."
for file in /docker-entrypoint-initdb.d/functions/*.sql; do
    if [ -f "$file" ]; then
        echo "  Executing: $(basename $file)"
        psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$file"
    fi
done
echo ""

# Verify installation
echo "============================================"
echo "Verifying installation..."
echo "============================================"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    -- Check extensions
    SELECT 'Extensions:' as check_type, extname, extversion 
    FROM pg_extension 
    WHERE extname IN ('postgis', 'plpython3u', 'pg_trgm', 'btree_gin');
    
    -- Check tables
    SELECT 'Tables:' as check_type, tablename 
    FROM pg_tables 
    WHERE schemaname = 'public' 
    AND tablename IN ('atom', 'atom_composition', 'atom_relation');
    
    -- Check indexes
    SELECT 'Indexes:' as check_type, indexname 
    FROM pg_indexes 
    WHERE schemaname = 'public' 
    AND indexname LIKE 'idx_%';
    
    -- Check functions
    SELECT 'Functions:' as check_type, proname 
    FROM pg_proc 
    WHERE pronamespace = 'public'::regnamespace 
    AND proname IN ('atomize_value', 'atomize_text', 'compute_spatial_position');
EOSQL

echo ""
echo "============================================"
echo "Hartonomous initialization complete!"
echo "============================================"
echo ""
echo "Quick start:"
echo "  1. Connect: psql -h localhost -U postgres -d hartonomous"
echo "  2. Atomize: SELECT atomize_text('Hello World');"
echo "  3. Explore: SELECT * FROM atom LIMIT 10;"
echo ""
