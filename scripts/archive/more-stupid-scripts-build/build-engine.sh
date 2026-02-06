#!/usr/bin/env bash
# ==============================================================================
# Build C++ Engine Only
# ==============================================================================
# Builds the C++ engine (libengine*.so, PostgreSQL extensions, tools)
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }

PRESET="${1:-linux-release-max-perf}"
JOBS=$(nproc 2>/dev/null || echo 8)

print_info "=== Building C++ Engine ==="
print_info "Preset: $PRESET"
print_info "Jobs: $JOBS"
echo ""

cd "$PROJECT_ROOT"

# Configure if needed
if [ ! -d "build/$PRESET" ]; then
    print_info "Configuring..."
    cmake --preset "$PRESET"
    print_success "Configuration complete"
    echo ""
fi

# Build
print_info "Building..."
if cmake --build "build/$PRESET" -j"$JOBS"; then
    echo ""
    print_success "C++ Engine build complete!"
else
    echo ""
    echo "Build failed"
    exit 1
fi
