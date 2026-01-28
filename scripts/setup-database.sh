#!/usr/bin/env bash
# Hartonomous Database Setup Script (Linux/macOS)
# Usage: ./setup-database.sh

set -e

# Default values
DB_NAME="hartonomous"
DB_USER="postgres"
DB_HOST="localhost"
DB_PORT="5432"
DROP_EXISTING=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

function print_success() {
    echo -e "${GREEN}$1${NC}"
}

function print_error() {
    echo -e "${RED}$1${NC}"
}

function print_info() {
    echo -e "${CYAN}$1${NC}"
}

function print_warning() {
    echo -e "${YELLOW}$1${NC}"
}

function show_help() {
    cat << EOF
Hartonomous Database Setup Script

Usage: ./setup-database.sh [OPTIONS]

Options:
  -n, --name <name>     Database name (default: hartonomous)
  -u, --user <user>     Database user (default: postgres)
  -h, --host <host>     Database host (default: localhost)
  -p, --port <port>     Database port (default: 5432)
  -d, --drop            Drop existing database if it exists
  --help                Show this help message

Examples:
  ./setup-database.sh                 # Create hartonomous database
  ./setup-database.sh --drop          # Drop and recreate
  ./setup-database.sh --name test_db  # Use custom database name

EOF
    exit 0
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -n|--name)
            DB_NAME="$2"
            shift 2
            ;;
        -u|--user)
            DB_USER="$2"
            shift 2
            ;;
        -h|--host)
            DB_HOST="$2"
            shift 2
            ;;
        -p|--port)
            DB_PORT="$2"
            shift 2
            ;;
        -d|--drop)
            DROP_EXISTING=true
            shift
            ;;
        --help)
            show_help
            ;;
        *)
            echo "Unknown option: $1"
            show_help
            ;;
    esac
done

print_info "=== Hartonomous Database Setup ==="
echo ""

# Check PostgreSQL
print_info "Checking PostgreSQL..."
if ! command -v psql &> /dev/null; then
    print_error "✗ PostgreSQL not found or not in PATH"
    print_error "  Please install PostgreSQL 15+ and add it to PATH"
    exit 1
fi

PG_VERSION=$(psql --version | grep -oP '\d+\.\d+' | head -1)
print_success "✓ PostgreSQL found: $PG_VERSION"

if (( $(echo "$PG_VERSION < 15.0" | bc -l) )); then
    print_warning "⚠ PostgreSQL 15+ recommended (found $PG_VERSION)"
fi

# Check PostGIS
print_info "Checking PostGIS..."
CHECK_POSTGIS="SELECT installed_version FROM pg_available_extensions WHERE name = 'postgis';"

POSTGIS_VERSION=$(psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -t -c "$CHECK_POSTGIS" 2>/dev/null | grep -oP '\d+\.\d+' || echo "")

if [ -n "$POSTGIS_VERSION" ]; then
    print_success "✓ PostGIS found: $POSTGIS_VERSION"
else
    print_warning "⚠ PostGIS not found"
    print_warning "  Please install PostGIS 3.3+"
    print_warning "  Ubuntu/Debian: sudo apt install postgresql-$PG_VERSION-postgis-3"
    print_warning "  macOS: brew install postgis"
fi

# Drop existing if requested
if [ "$DROP_EXISTING" = true ]; then
    echo ""
    print_info "Dropping existing database..."
    psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -c "DROP DATABASE IF EXISTS $DB_NAME;" 2>/dev/null || true
    print_success "✓ Dropped existing database"
fi

# Create database
echo ""
print_info "Creating database: $DB_NAME..."
if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -c "CREATE DATABASE $DB_NAME;"; then
    print_success "✓ Database created"
else
    print_error "✗ Could not create database"
    print_error "  Database may already exist. Use --drop to recreate."
    exit 1
fi

# Install PostGIS extension
echo ""
print_info "Installing PostGIS extension..."
if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -c "CREATE EXTENSION IF NOT EXISTS postgis;"; then
    print_success "✓ PostGIS extension installed"
else
    print_error "✗ Could not install PostGIS extension"
    print_error "  Make sure PostGIS is installed on your system"
    exit 1
fi

# Verify PostGIS
print_info "Verifying PostGIS..."
POSTGIS_INFO=$(psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -t -c "SELECT PostGIS_Version();" | xargs)
print_success "✓ PostGIS verified: $POSTGIS_INFO"

# Run schema scripts
echo ""
print_info "Applying database schema..."

SCHEMA_FILES=(
    "PostgresExtension/schema/hartonomous_schema.sql"
    "PostgresExtension/schema/relations_schema.sql"
    "PostgresExtension/schema/postgis_spatial_functions.sql"
    "PostgresExtension/schema/security_model.sql"
)

for SCHEMA_FILE in "${SCHEMA_FILES[@]}"; do
    if [ -f "$SCHEMA_FILE" ]; then
        print_info "  Applying $SCHEMA_FILE..."
        if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -f "$SCHEMA_FILE"; then
            print_success "  ✓ Applied $SCHEMA_FILE"
        else
            print_error "  ✗ Failed to apply $SCHEMA_FILE"
            exit 1
        fi
    else
        print_warning "  ⚠ Schema file not found: $SCHEMA_FILE"
    fi
done

# Create indexes
echo ""
print_info "Creating indexes..."
INDEX_SQL="
-- Spatial indexes
CREATE INDEX IF NOT EXISTS idx_atoms_s3_position
    ON atoms USING GIST (st_makepoint(s3_x, s3_y, s3_z));

CREATE INDEX IF NOT EXISTS idx_compositions_centroid
    ON compositions USING GIST (st_makepoint(centroid_x, centroid_y, centroid_z));

-- Hilbert indexes
CREATE INDEX IF NOT EXISTS idx_atoms_hilbert
    ON atoms USING BTREE (hilbert_index);

CREATE INDEX IF NOT EXISTS idx_compositions_hilbert
    ON compositions USING BTREE (hilbert_index);

-- Hash indexes
CREATE INDEX IF NOT EXISTS idx_atoms_hash
    ON atoms USING BTREE (hash);

CREATE INDEX IF NOT EXISTS idx_compositions_hash
    ON compositions USING BTREE (hash);

CREATE INDEX IF NOT EXISTS idx_relations_hash
    ON relations USING BTREE (hash);
"

if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -c "$INDEX_SQL"; then
    print_success "✓ Indexes created"
else
    print_error "✗ Failed to create indexes"
    exit 1
fi

# Verify installation
echo ""
print_info "Verifying installation..."
VERIFY_SQL="SELECT COUNT(*) AS table_count FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';"

TABLE_COUNT=$(psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -t -c "$VERIFY_SQL" | xargs)
print_success "✓ Verification complete: $TABLE_COUNT tables created"

# Summary
echo ""
print_success "=== Database Setup Complete ==="
echo ""
print_info "Database: $DB_NAME"
print_info "Host: $DB_HOST:$DB_PORT"
print_info "User: $DB_USER"
echo ""
print_info "Connection string:"
print_info "  psql -U $DB_USER -h $DB_HOST -p $DB_PORT -d $DB_NAME"
echo ""
print_info "Next steps:"
print_info "  1. Test connection: psql -U $DB_USER -d $DB_NAME -c 'SELECT PostGIS_Version();'"
print_info "  2. Run example: ./build/release-native/Engine/example_unicode_projection"
print_info "  3. Ingest data using ContentIngester API"
echo ""
