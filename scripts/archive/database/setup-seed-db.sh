#!/usr/bin/env bash
# ==============================================================================
# Setup Seed Database (Temporary)
# ==============================================================================
# Creates temporary database for UCDIngestor to populate with Unicode metadata.
# This is separate from the main 'hartonomous' database.
#
# The seed database contains:
#   - UCD (Unicode Character Database) data
#   - UCA (Unicode Collation Algorithm) data
#   - Semantic sequences, stroke counts, decompositions
#
# After seed_unicode tool runs, this database can be dropped.
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../lib/common.sh"

DB_NAME="ucd_seed"
DB_USER="${DB_USER:-postgres}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"

print_header "Setup Seed Database for UCDIngestor"

# Check if database exists
if psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -lqt | cut -d \| -f 1 | grep -qw "$DB_NAME"; then
    print_warning "Database '$DB_NAME' already exists. Dropping and recreating..."
    dropdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" "$DB_NAME"
fi

# Create database
print_step "Creating seed database: $DB_NAME"
createdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" "$DB_NAME"
print_success "Seed database created"

# Enable PostGIS (UCDIngestor might need spatial types)
print_step "Enabling PostGIS extension"
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "CREATE EXTENSION IF NOT EXISTS postgis;"
print_success "PostGIS enabled"

print_complete "Seed database ready for UCDIngestor"
echo ""
echo "Next step: Run UCDIngestor to populate Unicode metadata"
echo "  ./UCDIngestor/setup_db.sh"
