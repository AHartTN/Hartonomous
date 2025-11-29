#!/bin/bash
# ============================================================================
# Docker Entrypoint Database Initialization (Greenfield)
# 
# This script is executed by PostgreSQL docker-entrypoint when the database
# is first initialized. It loads the Hartonomous schema in the correct order.
#
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.
# ============================================================================

set -e

echo "============================================"
echo "  Hartonomous Schema Init (Greenfield)"
echo "============================================"

SCHEMA_DIR="/schema"

# Extensions
echo "→ Installing extensions..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    -- Citus for columnar storage (MUST be first)
    CREATE EXTENSION IF NOT EXISTS citus;
    CREATE EXTENSION IF NOT EXISTS citus_columnar;
    
    -- PostGIS and utilities
    CREATE EXTENSION IF NOT EXISTS postgis;
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    CREATE EXTENSION IF NOT EXISTS btree_gin;
    CREATE EXTENSION IF NOT EXISTS btree_gist;
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
    CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
EOSQL

# Try PL/Python (may fail depending on container image)
psql -v ON_ERROR_STOP=0 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS plpython3u;
EOSQL

# Custom types (optional)
if [ -d "$SCHEMA_DIR/types" ]; then
    echo "→ Creating custom types..."
    for f in $SCHEMA_DIR/types/*.sql; do
        [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
    done
fi

# Core tables (3-table architecture)
echo "→ Creating core tables..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/tables/001_atom.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/tables/002_atom_composition.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/tables/003_atom_relation.sql

# Additional tables (if needed)
if [ -f "$SCHEMA_DIR/core/tables/004_history_tables.sql" ]; then
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/tables/004_history_tables.sql
fi
if [ -f "$SCHEMA_DIR/core/tables/005_ooda_tables.sql" ]; then
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/tables/005_ooda_tables.sql
fi

# Core Functions (order matters: spatial functions before atomization)
echo "→ Creating spatial functions..."
for f in $SCHEMA_DIR/core/functions/spatial/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

echo "→ Creating atomization functions..."
for f in $SCHEMA_DIR/core/functions/atomization/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

echo "→ Creating composition functions..."
for f in $SCHEMA_DIR/core/functions/composition/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

echo "→ Creating relation functions..."
for f in $SCHEMA_DIR/core/functions/relations/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

# Optional function directories
for dir in ooda helpers gpu inference landmarks associations; do
    if [ -d "$SCHEMA_DIR/core/functions/$dir" ]; then
        echo "→ Creating $dir functions..."
        for f in $SCHEMA_DIR/core/functions/$dir/*.sql; do
            [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
        done
    fi
done

# Provenance functions (require AGE extension - skip if not available)
if [ -d "$SCHEMA_DIR/core/functions/provenance" ]; then
    echo "→ Creating provenance functions (optional, requires AGE)..."
    for f in $SCHEMA_DIR/core/functions/provenance/*.sql; do
        [ -f "$f" ] && psql -v ON_ERROR_STOP=0 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f" 2>/dev/null || echo "  (Skipped: $(basename $f) - requires AGE extension)"
    done
fi

# Core Indexes (dual indexing strategy: GiST + Hilbert B-tree)
echo "→ Creating spatial indexes..."
for f in $SCHEMA_DIR/core/indexes/spatial/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

echo "→ Creating core indexes..."
for f in $SCHEMA_DIR/core/indexes/core/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

echo "→ Creating composition indexes..."
for f in $SCHEMA_DIR/core/indexes/composition/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

echo "→ Creating relation indexes..."
for f in $SCHEMA_DIR/core/indexes/relations/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

# Triggers (reference counting, temporal versioning, spatial Hilbert auto-compute)
echo "→ Creating triggers..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/triggers/001_temporal_versioning.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/triggers/002_reference_counting.sql
if [ -f "$SCHEMA_DIR/core/triggers/003_provenance_notify.sql" ]; then
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/triggers/003_provenance_notify.sql
fi
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f $SCHEMA_DIR/core/triggers/004_spatial_hilbert.sql

# Views (optional convenience)
if [ -d "$SCHEMA_DIR/views" ]; then
    echo "→ Creating views..."
    for f in $SCHEMA_DIR/views/*.sql; do
        [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
    done
fi

# Verification
echo ""
echo "============================================"
echo "  Verifying Installation"
echo "============================================"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    \echo 'Extensions:'
    SELECT extname, extversion FROM pg_extension 
    WHERE extname IN ('citus', 'citus_columnar', 'postgis', 'pg_trgm', 'btree_gin', 'btree_gist', 'pgcrypto')
    ORDER BY extname;
    
    \echo ''
    \echo 'Core Tables:'
    SELECT tablename FROM pg_tables 
    WHERE schemaname = 'public' 
    AND tablename IN ('atom', 'atom_composition', 'atom_relation')
    ORDER BY tablename;
    
    \echo ''
    \echo 'Spatial Columns (should show POINTZM):'
    SELECT f_table_name, f_geometry_column, type, coord_dimension
    FROM geometry_columns
    WHERE f_table_schema = 'public'
    ORDER BY f_table_name;
    
    \echo ''
    \echo 'Spatial Indexes (dual strategy):'
    SELECT indexname FROM pg_indexes 
    WHERE schemaname = 'public' 
    AND (indexname LIKE '%spatial%' OR indexname LIKE '%hilbert%')
    ORDER BY indexname;
    
    \echo ''
    \echo 'Key Functions:'
    SELECT proname FROM pg_proc 
    WHERE pronamespace = 'public'::regnamespace 
    AND proname IN ('atomize_value', 'compute_spatial_position', 'hilbert_index_3d',
                    'create_composition', 'create_relation')
    ORDER BY proname;
    
    \echo ''
    \echo 'Triggers:'
    SELECT tgname, tgrelid::regclass FROM pg_trigger
    WHERE tgisinternal = false
    ORDER BY tgrelid::regclass::text, tgname;
EOSQL

echo ""
echo "✓ Schema initialization complete"
echo ""
