#!/usr/bin/env bash
# ==============================================================================
# Create Hartonomous Database
# ==============================================================================
# Creates the hartonomous database and schema
# ==============================================================================

set -e

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠ $1${NC}"; }

DB_NAME="hartonomous"
DB_USER="${POSTGRES_USER:-postgres}"

print_info "=== Creating Hartonomous Database ==="
echo ""

# Check if PostgreSQL is running
if ! pg_isready -q; then
    echo "PostgreSQL is not running"
    echo "Start it with: sudo systemctl start postgresql"
    exit 1
fi

# Check if database already exists
if psql -U "$DB_USER" -lqt | cut -d \| -f 1 | grep -qw "$DB_NAME"; then
    print_warning "Database '$DB_NAME' already exists"
    echo ""
    echo "To recreate it, run: ./scripts/database/reset-db.sh"
    exit 0
fi

# Create database
print_info "Creating database '$DB_NAME'..."
createdb -U "$DB_USER" "$DB_NAME"
print_success "Database created"

# Create schema
print_info "Creating schema..."
psql -U "$DB_USER" -d "$DB_NAME" -c "CREATE SCHEMA IF NOT EXISTS$hartonomous;"
psql -U "$DB_USER" -d "$DB_NAME" -c "ALTER DATABASE $DB_NAME SET search_path TO hartonomous, public;"
print_success "Schema created"

echo ""
print_success "Database '$DB_NAME' ready!"
echo ""
echo "Next steps:"
echo "  1. Load extensions: ./scripts/database/load-extensions.sh"
echo "  2. Initialize schema: ./scripts/database/init-schema.sh"
