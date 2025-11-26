#!/bin/bash
# ============================================================================
# Docker Entrypoint Database Initialization
# 
# This script is executed by PostgreSQL docker-entrypoint when the database
# is first initialized. It loads the Hartonomous schema in the correct order.
#
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.
# ============================================================================

set -e

echo "???????????????????????????????????????????????????????????"
echo "  Hartonomous Schema Initialization (Docker)"
echo "???????????????????????????????????????????????????????????"

SCHEMA_DIR="/docker-entrypoint-initdb.d"

# Extensions
echo "? Installing extensions..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS postgis;
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    CREATE EXTENSION IF NOT EXISTS btree_gin;
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EOSQL

# Try PL/Python (may fail, that's OK)
psql -v ON_ERROR_STOP=0 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS plpython3u;
EOSQL

# Custom types
echo "? Creating custom types..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/types/001_modality_type.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/types/002_relation_type.sql

# Core tables
echo "? Creating tables..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/core/tables/001_atom.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/core/tables/002_atom_composition.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/core/tables/003_atom_relation.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/core/tables/004_history_tables.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/core/tables/005_ooda_tables.sql

# Indexes
echo "? Creating indexes..."
for f in /schema/core/indexes/core/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done
for f in /schema/core/indexes/spatial/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done
for f in /schema/core/indexes/relations/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done
for f in /schema/core/indexes/composition/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

# Functions
echo "? Creating functions..."
for f in /schema/core/functions/atomization/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done
for f in /schema/core/functions/spatial/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done
for f in /schema/core/functions/relations/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done
for f in /schema/core/functions/composition/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done
for f in /schema/core/functions/ooda/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done
for f in /schema/core/functions/provenance/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

# Triggers
echo "? Creating triggers..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/core/triggers/001_temporal_versioning.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/core/triggers/002_reference_counting.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /schema/core/triggers/003_provenance_notify.sql

# Views
echo "? Creating views..."
for f in /schema/views/*.sql; do
    [ -f "$f" ] && psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f "$f"
done

echo "? Schema initialization complete"
