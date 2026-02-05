#!/usr/bin/env bash
# ==============================================================================
# Run All Tests
# ==============================================================================
# Runs unit, integration, and e2e tests in sequence
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RE='\033[0;31m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠ $1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }

FAILED=0

print_info "=== Running All Test Suites ==="
echo ""

# Unit Tests (fast, no dependencies)
print_info "[1/3] Unit Tests..."
if "$SCRIPT_DIR/run-unit-tests.sh" "$@"; then
    print_success "Unit tests passed"
else
    print_error "Unit tests failed"
    FAILED=$((FAILED + 1))
fi
echo ""

# Integration Tests (requires database)
print_info "[2/3] Integration Tests..."
if "$SCRIPT_DIR/run-integration-tests.sh" "$@"; then
    print_success "Integration tests passed"
else
    print_warning "Integration tests failed (database may not be set up)"
    FAILED=$((FAILED + 1))
fi
echo ""

# E2E Tests (full pipeline)
print_info "[3/3] End-to-End Tests..."
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="${PROJECT_ROOT}/build/linux-release-max-perf"
if [ -d "$BUILD_DIR" ]; then
    cd "$BUILD_DIR"
    if /usr/bin/ctest --output-on-failure -L e2e "$@"; then
        print_success "E2E tests passed"
    else
        print_warning "E2E tests failed"
        FAILED=$((FAILED + 1))
    fi
else
    print_warning "Build directory not found, skipping E2E tests"
    FAILED=$((FAILED + 1))
fi

echo ""
echo "=== Test Summary ==="
if [ $FAILED -eq 0 ]; then
    print_success "All test suites passed!"
    exit 0
else
    print_error "$FAILED test suite(s) failed"
    exit 1
fi
