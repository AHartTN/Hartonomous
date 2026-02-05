#!/usr/bin/env bash
# ==============================================================================
# Load PostgreSQL Extensions
# ==============================================================================
# Loads s3.so and hartonomous.so extensions into the database
# Requires: Extensions installed (via symlinks or system install)
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
RED='\033[0;31m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }

DB_NAME="hartonomous"
DB_USER="${POSTGRES_USER:-postgres}"

print_info "=== Loading PostgreSQL Extensions ==="
echo ""

# Check if database exists
if ! psql -U "$DB_USER" -lqt | cut -d \| -f 1 | grep -qw "$DB_NAME"; then
    print_error "Database '$DB_NAME' does not exist"
    echo "Create it first: ./scripts/database/create-db.sh"
    exit 1
fi

# Load s3 extension (S³ geometry)
print_info "Loading s3 extension..."
if psql -U "$DB_USER" -d "$DB_NAME" -c "CREATE EXTENSION IF NOT EXISTS s3 CASCADE;" 2>/dev/null; then
    print_success "s3 extension loaded"
else
    print_error "Failed to load s3 extension"
    echo "Make sure s3.so is installed (check /usr/lib/postgresql/18/lib/)"
    exit 1
fi

# Load hartonomous extension (main functionality)
print_info "Loading hartonomous extension..."
if psql -U "$DB_USER" -d "$DB_NAME" -c "CREATE EXTENSION IF NOT EXISTS hartonomous CASCADE;" 2>/dev/null; then
    print_success "hartonomous extension loaded"
else
    print_error "Failed to load hartonomous extension"
    echo "Make sure hartonomous.so is installed (check /usr/lib/postgresql/18/lib/)"
    exit 1
fi

echo ""
print_success "Extensions loaded successfully!"
echo ""
echo "Verify with:"
echo "  psql -U $DB_USER -d $DB_NAME -c '\\dx'"
