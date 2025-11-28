#!/bin/bash
# ============================================================================
# Hartonomous Database Initialization (Greenfield)
# Author: Anthony Hart
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.
# 
# Executes schema files in correct dependency order
# ============================================================================

set -e

echo "============================================"
echo "Hartonomous Database - Greenfield Init"
echo "============================================"

# Color codes for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to execute SQL file
execute_sql() {
    local file=$1
    local description=$2
    echo -e "${BLUE}→${NC} $description: ${GREEN}$(basename $file)${NC}"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$file"
}

# Function to execute all files in a directory
execute_directory() {
    local dir=$1
    local description=$2
    
    if [ -d "$dir" ]; then
        echo ""
        echo -e "${YELLOW}▶ $description${NC}"
        for file in "$dir"/*.sql; do
            if [ -f "$file" ]; then
                execute_sql "$file" "  "
            fi
        done
    fi
}

# Function to execute all files in nested directories
execute_nested_directory() {
    local base_dir=$1
    local description=$2
    
    if [ -d "$base_dir" ]; then
        echo ""
        echo -e "${YELLOW}▶ $description${NC}"
        find "$base_dir" -type f -name "*.sql" | sort | while read file; do
            execute_sql "$file" "  "
        done
    fi
}

# 1. Extensions
echo ""
echo -e "${YELLOW}▶ Enabling PostgreSQL Extensions${NC}"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS postgis;
    CREATE EXTENSION IF NOT EXISTS plpython3u;
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    CREATE EXTENSION IF NOT EXISTS btree_gin;
    CREATE EXTENSION IF NOT EXISTS btree_gist;
    CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
EOSQL
echo -e "${GREEN}✓${NC} Extensions enabled: postgis, plpython3u, pg_trgm, btree_gin, btree_gist, fuzzystrmatch"

# 2. Core Tables (3-table architecture)
execute_directory "/docker-entrypoint-initdb.d/core/tables" "Creating Core Tables"

# 3. Core Functions (atomization, spatial, composition, relations)
execute_nested_directory "/docker-entrypoint-initdb.d/core/functions" "Creating Core Functions"

# 4. Core Indexes (spatial dual indexing, composition, relations)
execute_nested_directory "/docker-entrypoint-initdb.d/core/indexes" "Creating Core Indexes"

# 5. Core Triggers (reference counting, temporal versioning, spatial Hilbert)
execute_directory "/docker-entrypoint-initdb.d/core/triggers" "Creating Core Triggers"

# 6. Views (optional - for convenience)
if [ -d "/docker-entrypoint-initdb.d/views" ]; then
    execute_nested_directory "/docker-entrypoint-initdb.d/views" "Creating Views"
fi

# 7. Optimizations (optional)
if [ -d "/docker-entrypoint-initdb.d/optimizations" ]; then
    execute_nested_directory "/docker-entrypoint-initdb.d/optimizations" "Applying Optimizations"
fi

# Verification
echo ""
echo "============================================"
echo -e "${YELLOW}▶ Verifying Installation${NC}"
echo "============================================"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    -- Extensions
    \echo '${BLUE}Extensions:${NC}'
    SELECT extname, extversion 
    FROM pg_extension 
    WHERE extname IN ('postgis', 'plpython3u', 'pg_trgm', 'btree_gin', 'btree_gist')
    ORDER BY extname;
    
    -- Tables
    \echo ''
    \echo '${BLUE}Core Tables:${NC}'
    SELECT tablename, 
           pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as size
    FROM pg_tables 
    WHERE schemaname = 'public' 
    AND tablename IN ('atom', 'atom_composition', 'atom_relation')
    ORDER BY tablename;
    
    -- Spatial columns
    \echo ''
    \echo '${BLUE}Spatial Columns:${NC}'
    SELECT f_table_name, f_geometry_column, type, srid, coord_dimension
    FROM geometry_columns
    WHERE f_table_schema = 'public'
    ORDER BY f_table_name;
    
    -- Indexes
    \echo ''
    \echo '${BLUE}Spatial Indexes:${NC}'
    SELECT indexname, tablename
    FROM pg_indexes 
    WHERE schemaname = 'public' 
    AND (indexname LIKE '%spatial%' OR indexname LIKE '%hilbert%')
    ORDER BY tablename, indexname;
    
    -- Key Functions
    \echo ''
    \echo '${BLUE}Key Functions:${NC}'
    SELECT proname as function_name
    FROM pg_proc 
    WHERE pronamespace = 'public'::regnamespace 
    AND proname IN ('atomize_value', 'atomize_text', 'compute_spatial_position', 
                    'hilbert_index_3d', 'create_composition', 'create_relation')
    ORDER BY proname;
    
    -- Triggers
    \echo ''
    \echo '${BLUE}Active Triggers:${NC}'
    SELECT tgname as trigger_name, tgrelid::regclass as table_name
    FROM pg_trigger
    WHERE tgisinternal = false
    ORDER BY tgrelid::regclass::text, tgname;
EOSQL

echo ""
echo "============================================"
echo -e "${GREEN}✓ Hartonomous initialization complete!${NC}"
echo "============================================"
echo ""
echo -e "${BLUE}Architecture:${NC}"
echo "  • 3 Core Tables: atom, atom_composition, atom_relation"
echo "  • POINTZM Geometry: (X,Y,Z) = semantic position, M = Hilbert index"
echo "  • Dual Indexing: GiST on XYZ (exact NN), B-tree on M (approximate NN)"
echo "  • Content Addressing: SHA-256, ≤64 byte atoms, global deduplication"
echo ""
echo -e "${BLUE}Quick Start:${NC}"
echo "  1. Connect:   psql -h localhost -U \$POSTGRES_USER -d \$POSTGRES_DB"
echo "  2. Atomize:   SELECT atomize_text('Hello World');"
echo "  3. Position:  UPDATE atom SET spatial_key = compute_spatial_position(atom_id) WHERE atom_id = 1;"
echo "  4. Query:     SELECT * FROM atom WHERE spatial_key IS NOT NULL LIMIT 10;"
echo "  5. KNN:       SELECT * FROM atom ORDER BY spatial_key <-> ST_MakePoint(0,0,0) LIMIT 10;"
echo ""

