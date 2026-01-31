#!/bin/bash
# ==============================================================================
# Hartonomous Database Initialization Script
# ==============================================================================

set -e  # Exit on error

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

# Drop existing database
echo "Dropping existing database (if exists)..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres <<EOF
DROP DATABASE IF EXISTS $DB_NAME;
EOF

# Create fresh database
echo "Creating database..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres <<EOF
CREATE DATABASE $DB_NAME;
EOF

# Connect to new database and set up extensions
echo "Setting up extensions..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<EOF
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
EOF

# Load custom types
echo "Loading custom types..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$SQL_DIR/types/00-custom_types.sql"

# Load table schemas in dependency order
echo "Creating tables..."
for schema_file in \
    "$SQL_DIR/tables/01-Tenant.sql" \
    "$SQL_DIR/tables/02-User.sql" \
    "$SQL_DIR/tables/03-Content.sql" \
    "$SQL_DIR/tables/04-Physicality.sql" \
    "$SQL_DIR/tables/05-Atom.sql" \
    "$SQL_DIR/tables/06-Composition.sql" \
    "$SQL_DIR/tables/07-CompositionSequence.sql" \
    "$SQL_DIR/tables/08-Relation.sql" \
    "$SQL_DIR/tables/09-RelationSequence.sql" \
    "$SQL_DIR/tables/10-RelationRating.sql" \
    "$SQL_DIR/tables/11-RelationEvidence.sql"
do
    if [ -f "$schema_file" ]; then
        echo "  Loading $(basename "$schema_file")..."
        psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$schema_file"
    else
        echo "  WARNING: $schema_file not found, skipping..."
    fi
done

# Load functions
echo "Creating functions..."
for func_dir in "$SQL_DIR/functions"/*; do
    if [ -d "$func_dir" ]; then
        for func_file in "$func_dir"/*.sql; do
            if [ -f "$func_file" ]; then
                echo "  Loading $(basename "$func_file")..."
                psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$func_file"
            fi
        done
    fi
done

# Load standalone functions
for func_file in "$SQL_DIR/functions"/*.sql; do
    if [ -f "$func_file" ]; then
        echo "  Loading $(basename "$func_file")..."
        psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$func_file"
    fi
done

# Load views
echo "Creating views..."
for view_file in "$SQL_DIR/views"/*.sql; do
    if [ -f "$view_file" ]; then
        echo "  Loading $(basename "$view_file")..."
        psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$view_file"
    fi
done

# Install Hartonomous extension (if built)
EXTENSION_SO="/home/ahart/Projects/Hartonomous/build/linux-release-max-perf/PostgresExtension/hartonomous.so"
if [ -f "$EXTENSION_SO" ]; then
    echo "Installing Hartonomous extension..."
    # Copy to PostgreSQL extension directory
    PG_LIB_DIR=$(pg_config --pkglibdir)
    sudo cp "$EXTENSION_SO" "$PG_LIB_DIR/"

    # Create extension control file
    PG_SHARE_DIR=$(pg_config --sharedir)
    sudo mkdir -p "$PG_SHARE_DIR/extension"

    cat > /tmp/hartonomous.control <<EOF
comment = 'Hartonomous: Universal Substrate for Intelligence'
default_version = '0.1.0'
module_pathname = '\$libdir/hartonomous'
relocatable = true
EOF
    sudo cp /tmp/hartonomous.control "$PG_SHARE_DIR/extension/"

    # Create extension
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<EOF
CREATE EXTENSION IF NOT EXISTS hartonomous;
EOF
else
    echo "WARNING: Hartonomous extension not found at $EXTENSION_SO"
    echo "Build the extension first with: cmake --build build/linux-release-max-perf"
fi

echo "========================================="
echo "Database initialization complete!"
echo "========================================="
echo ""
echo "Next steps:"
echo "  1. Seed Unicode codepoints: ./build/linux-release-max-perf/Engine/tools/seed_unicode"
echo "  2. Test ingestion: Use the text_ingester API"
echo ""
