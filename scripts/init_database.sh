#!/bin/bash
# ==============================================================================
# Hartonomous Database Initialization Script
# ==============================================================================

set -e

DB_NAME="${HARTONOMOUS_DB:-hartonomous}"
DB_USER="${HARTONOMOUS_USER:-$USER}"
DB_HOST="${HARTONOMOUS_HOST:-localhost}"
DB_PORT="${HARTONOMOUS_PORT:-5432}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SQL_DIR="$SCRIPT_DIR/sql"

echo "========================================="
echo "Hartonomous Database Initialization"
echo "========================================="
echo "Database: $DB_NAME"
echo "User: $DB_USER"
echo "Host: $DB_HOST"
echo "Port: $DB_PORT"
echo "========================================="

# ------------------------------------------------------------------------------
# Install Hartonomous extension files BEFORE creating the database
# ------------------------------------------------------------------------------

EXTENSION_SO="$SCRIPT_DIR/../PostgresExtension/hartonomous/hartonomous.so"
EXTENSION_SQL="$SCRIPT_DIR/../PostgresExtension/hartonomous/dist/hartonomous--0.1.0.sql"

if [ -f "$EXTENSION_SO" ]; then
    echo "Installing Hartonomous extension files..."

    PG_LIB_DIR=$(pg_config --pkglibdir)
    PG_SHARE_DIR=$(pg_config --sharedir)

    sudo cp "$EXTENSION_SO" "$PG_LIB_DIR/"
    sudo mkdir -p "$PG_SHARE_DIR/extension"
    sudo cp "$EXTENSION_SQL" "$PG_SHARE_DIR/extension/"

    cat > /tmp/hartonomous.control <<EOF
comment = 'Hartonomous: Universal Substrate for Intelligence'
default_version = '0.1.0'
module_pathname = '\$libdir/hartonomous'
relocatable = true
EOF

    sudo cp /tmp/hartonomous.control "$PG_SHARE_DIR/extension/"
else
    echo "ERROR: Hartonomous extension not found at $EXTENSION_SO"
    echo "Build it first: cmake --build build/linux-release-max-perf"
    exit 1
fi

# ------------------------------------------------------------------------------
# Drop and recreate database
# ------------------------------------------------------------------------------

echo "Dropping existing database..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres <<EOF
DROP DATABASE IF EXISTS $DB_NAME;
EOF

echo "Creating database..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres <<EOF
CREATE DATABASE $DB_NAME;
EOF

# ------------------------------------------------------------------------------
# Install extensions inside the new database
# ------------------------------------------------------------------------------

echo "Setting up extensions..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<EOF
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS hartonomous;
EOF

# ------------------------------------------------------------------------------
# Load schema (tables only)
# ------------------------------------------------------------------------------

echo "Creating tables..."
for schema_file in "$SQL_DIR"/tables/*.sql; do
    if [ -f "$schema_file" ]; then
        echo "  Loading $(basename "$schema_file")..."
        psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$schema_file"
    fi
done

# ------------------------------------------------------------------------------
# Load views
# ------------------------------------------------------------------------------

echo "Creating views..."
for view_file in "$SQL_DIR/views"/*.sql; do
    if [ -f "$view_file" ]; then
        echo "  Loading $(basename "$view_file")..."
        psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$view_file"
    fi
done

echo "========================================="
echo "Database initialization complete!"
echo "========================================="
echo ""
echo "Next steps:"
echo "  1. Seed Unicode codepoints: ./build/linux-release-max-perf/Engine/tools/seed_unicode"
echo "  2. Test ingestion: Use the text_ingester API"
echo ""
