#!/usr/bin/env bash
# ==============================================================================
# Run Integration Tests
# ==============================================================================
# Runs tests that require external services (PostgreSQL database)
# Requires: hartonomous database to exist with extensions loaded
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="${PROJECT_ROOT}/build/linux-release-max-perf"

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠ $1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }

# Check if build exists
if [ ! -d "$BUILD_DIR" ]; then
    print_error "Build directory not found: $BUILD_DIR"
    echo "Run './scripts/build/build-all.sh' first"
    exit 1
fi

# Check if PostgreSQL is running
if ! pg_isready -q; then
    print_error "PostgreSQL is not running"
    echo "Start PostgreSQL first: sudo systemctl start postgresql"
    exit 1
fi

# Check if hartonomous database exists
if ! psql -U postgres -lqt | cut -d \| -f 1 | grep -qw hartonomous; then
    print_warning "Database 'hartonomous' does not exist"
    echo ""
    echo "Create it with:"
    echo "  ./scripts/database/create-db.sh"
    echo ""
    exit 1
fi

cd "$BUILD_DIR"

print_info "=== Running Integration Tests (Requires Database) ==="
echo ""

# Run only tests labeled "integration"
if /usr/bin/ctest --output-on-failure -L integration "$@"; then
    echo ""
    print_success "All integration tests passed!"
    exit 0
else
    echo ""
    print_error "Integration tests failed"
    exit 1
fi
