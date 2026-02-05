#!/usr/bin/env bash
# ==============================================================================
# Create Hartonomous Database (Main Intelligence Substrate)
# ==============================================================================
# Creates the main 'hartonomous' database with proper schema.
# This is the permanent database where geometric intelligence resides.
#
# Schema Structure:
#   - 00-foundation.sql: Extensions, schemas, base types
#   - 01-core-tables.sql: Atom, Composition, Relation tables
#   - 02-functions.sql: SQL functions for querying
#
# Atoms will be populated by seed_unicode tool from seed database.
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$SCRIPT_DIR/../lib/common.sh"

DB_NAME="hartonomous"
DB_USER="${DB_USER:-postgres}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DROP_EXISTING=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -d|--drop) DROP_EXISTING=true; shift ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

print_header "Create Hartonomous Database"

# Drop existing if requested
if [ "$DROP_EXISTING" = true ]; then
    if psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -lqt | cut -d \| -f 1 | grep -qw "$DB_NAME"; then
        print_warning "Dropping existing database: $DB_NAME"
        psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres -c "
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = '$DB_NAME' AND pid <> pg_backend_pid();
        " 2>/dev/null || true
        sleep 0.5
        dropdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" "$DB_NAME"
        print_success "Database dropped"
    fi
fi

# Create database
if ! psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -lqt | cut -d \| -f 1 | grep -qw "$DB_NAME"; then
    print_step "Creating database: $DB_NAME"
    createdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" "$DB_NAME"
    print_success "Database created"
else
    print_info "Database already exists: $DB_NAME"
fi

# Load schema
SQL_DIR="$PROJECT_ROOT/scripts/sql"

print_step "Loading foundation schema"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$SQL_DIR/00-foundation.sql"
print_success "Foundation loaded"

print_step "Loading core tables"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$SQL_DIR/01-core-tables.sql"
print_success "Core tables created"

print_step "Loading functions"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$SQL_DIR/02-functions.sql"
print_success "Functions loaded"

print_complete "Hartonomous database ready"
echo ""
echo "Database: $DB_NAME"
echo "Schema: hartonomous"
echo ""
echo "Next steps:"
echo "  1. Populate Atoms: ./scripts/database/populate-atoms.sh"
echo "  2. Ingest embeddings: ./scripts/ingest/ingest-embeddings.sh"
echo "  3. Ingest text: ./scripts/ingest/ingest-text.sh"
