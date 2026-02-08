#!/usr/bin/env bash
# ==============================================================================
# Hartonomous Database Setup Script
# ==============================================================================
# Uses structured SQL files with \i directives (proper approach)
# NOT blindly loading all files in subdirectories (Gemini sabotage)

set -e

# Default values
DB_NAME="${DB_NAME:-hartonomous}"
DB_USER="${DB_USER:-postgres}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DROP_EXISTING=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

print_success() { echo -e "${GREEN}$1${NC}"; }
print_error() { echo -e "${RED}$1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_warning() { echo -e "${YELLOW}$1${NC}"; }

show_help() {
    cat << EOF
Hartonomous Database Setup Script

Usage: ./03-setup-database.sh [OPTIONS]

Options:
  -n, --name <name>     Database name (default: hartonomous)
  -u, --user <user>     Database user (default: postgres)
  -h, --host <host>     Database host (default: localhost)
  -p, --port <port>     Database port (default: 5432)
  -d, --drop            Drop existing database if it exists
  --help                Show this help message

Examples:
  ./03-setup-database.sh                 # Create hartonomous database
  ./03-setup-database.sh --drop          # Drop and recreate
  ./03-setup-database.sh --name test_db  # Use custom database name

EOF
    exit 0
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -n|--name) DB_NAME="$2"; shift 2 ;;
        -u|--user) DB_USER="$2"; shift 2 ;;
        -h|--host) DB_HOST="$2"; shift 2 ;;
        -p|--port) DB_PORT="$2"; shift 2 ;;
        -d|--drop) DROP_EXISTING=true; shift ;;
        --help) show_help ;;
        *) echo "Unknown option: $1"; show_help ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SQL_DIR="$(cd "$SCRIPT_DIR/../sql" && pwd)"

print_info "=== Hartonomous Database Setup ==="
echo ""

# Check PostgreSQL
print_info "Checking PostgreSQL..."
if ! command -v psql &> /dev/null; then
    print_error "✗ PostgreSQL not found"
    exit 1
fi

PG_VERSION=$(psql --version | grep -oP '\d+\.\d+' | head -1)
print_success "✓ PostgreSQL found: $PG_VERSION"

# Check PostGIS
CHECK_POSTGIS="SELECT installed_version FROM pg_available_extensions WHERE name = 'postgis';"
POSTGIS_VERSION=$(psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -t -c "$CHECK_POSTGIS" 2>/dev/null | grep -oP '\d+\.\d+' || echo "")

if [ -n "$POSTGIS_VERSION" ]; then
    print_success "✓ PostGIS found: $POSTGIS_VERSION"
else
    print_warning "⚠ PostGIS not found"
fi

# Drop existing if requested
if [ "$DROP_EXISTING" = true ]; then
    echo ""
    print_info "Dropping existing database..."
    psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -c "
        SELECT pg_terminate_backend(pid)
        FROM pg_stat_activity
        WHERE datname = '$DB_NAME' AND pid <> pg_backend_pid();
    " 2>/dev/null || true
    sleep 0.5
    psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -c "DROP DATABASE IF EXISTS $DB_NAME;" 2>/dev/null || true
    print_success "✓ Dropped existing database"
fi

# Create database
echo ""
print_info "Creating database: $DB_NAME..."
if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d postgres -c "CREATE DATABASE $DB_NAME;"; then
    print_success "✓ Database created"
else
    print_error "✗ Could not create database (may already exist)"
    print_info "Use --drop to recreate"
    exit 1
fi

# Create hartonomous schema
echo ""
print_info "Creating hartonomous schema..."
psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -c "CREATE SCHEMA IF NOT EXISTS hartonomous;"
psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -c "CREATE SCHEMA IF NOT EXISTS hartonomous_internal;"
print_success "✓ Schemas created"

# Load Structured SQL Files (proper approach with \i directives)
echo ""
print_info "Loading schema objects..."

cd "$SQL_DIR"

# Step 1: Foundation (extensions, domains, types)
print_info "  [1/4] Loading foundation..."
if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -f "00-foundation.sql"; then
    print_success "    ✓ Foundation loaded"
else
    print_error "    ✗ Foundation failed"
    exit 1
fi

# Step 2: UCD Schema (Gene Pool)
print_info "  [2/4] Loading UCD schema..."
if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -f "tables/00-UCD-Metadata.sql"; then
    print_success "    ✓ UCD schema loaded"
else
    print_error "    ✗ UCD schema failed"
    exit 1
fi

# Step 3: Core Tables
print_info "  [3/4] Loading core tables..."
if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -f "01-core-tables.sql"; then
    print_success "    ✓ Core tables loaded"
else
    print_error "    ✗ Core tables failed"
    exit 1
fi

# Step 4: Functions
print_info "  [4/4] Loading functions..."
if psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -f "02-functions.sql" 2>&1 | tee /tmp/functions-load.log; then
    print_success "    ✓ Functions loaded"
    if grep -q "ERROR" /tmp/functions-load.log; then
        print_warning "    ⚠ Some functions had errors (check log)"
    fi
else
    print_warning "    ⚠ Some functions failed (continuing anyway)"
fi

# Set default search_path
psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -c "ALTER DATABASE $DB_NAME SET search_path TO hartonomous, public;"

# Verify installation
echo ""
print_info "Verifying installation..."
VERIFY_SQL="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'hartonomous';"
TABLE_COUNT=$(psql -U "$DB_USER" -h "$DB_HOST" -p "$DB_PORT" -d "$DB_NAME" -t -c "$VERIFY_SQL" | xargs)

if [ "$TABLE_COUNT" -ge "11" ]; then
    print_success "✓ Verification complete: $TABLE_COUNT tables in hartonomous schema"
else
    print_error "✗ Verification failed: only $TABLE_COUNT tables found (expected >= 11)"
    exit 1
fi

# Summary
echo ""
print_success "=== Database Setup Complete ==="
echo ""
print_info "Database: $DB_NAME"
print_info "Host: $DB_HOST:$DB_PORT"
print_info "User: $DB_USER"
print_info "Tables: $TABLE_COUNT"
echo ""
print_info "Connection:"
print_info "  psql -U $DB_USER -h $DB_HOST -p $DB_PORT -d $DB_NAME"
echo ""
print_info "Next steps:"
print_info "  1. Seed Unicode: ./scripts/linux/05-seed-unicode.sh"
print_info "  2. Ingest WordNet/OMW: ./scripts/linux/06-ingest-wordnet-omw.sh"
print_info "  3. Ingest data: ./scripts/linux/04-run_ingestion.sh"
echo ""
