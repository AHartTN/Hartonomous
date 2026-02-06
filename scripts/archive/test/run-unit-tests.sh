#!/usr/bin/env bash
# ==============================================================================
# Run Unit Tests Only
# ==============================================================================
# Runs pure logic tests with no external dependencies (no database required)
# Fast feedback loop for development
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="${PROJECT_ROOT}/build/linux-release-max-perf"

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
RED='\033[0;31m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }

# Check if build exists
if [ ! -d "$BUILD_DIR" ]; then
    print_error "Build directory not found: $BUILD_DIR"
    echo "Run './scripts/build/build-all.sh' first"
    exit 1
fi

cd "$BUILD_DIR"

print_info "=== Running Unit Tests (No External Dependencies) ==="
echo ""

# Run only tests labeled "unit"
if /usr/bin/ctest --output-on-failure -L unit "$@"; then
    echo ""
    print_success "All unit tests passed!"
    exit 0
else
    echo ""
    print_error "Unit tests failed"
    exit 1
fi
