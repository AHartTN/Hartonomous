#!/usr/bin/env bash
# Hartonomous Database Setup Script (Linux/macOS)
# Usage: ./setup-database.sh

set -e

# Default values
DB_NAME="hypercube"
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

if awk "BEGIN {exit !($PG_VERSION < 15.0)}"; then
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

# Create schema
echo ""
print_info "Creating hartonomous schema..."
psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -c "CREATE SCHEMA IF NOT EXISTS hartonomous;"

# Install extensions
echo ""
print_info "Installing extensions..."
for ext in postgis s3 hartonomous; do
    print_info "  Installing $ext..."
    if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -c "CREATE EXTENSION IF NOT EXISTS $ext CASCADE;"; then
        print_success "  ✓ $ext extension installed"
    else
        print_error "  ✗ Could not install $ext extension"
        exit 1
    fi
done

# Set search_path
psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -c "ALTER DATABASE $DB_NAME SET search_path TO public, hartonomous;"

# Verify installation
echo ""
print_info "Verifying installation..."
# Check for a specific table from the hartonomous extension
VERIFY_SQL="SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'physicality' AND table_schema = 'hartonomous';"

TABLE_EXISTS=$(psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -t -c "$VERIFY_SQL" | xargs)
if [ "$TABLE_EXISTS" -eq "1" ]; then
    print_success "✓ Verification complete: Hartonomous schema is present"
else
    print_error "✗ Verification failed: Physicality table not found"
    exit 1
fi

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
