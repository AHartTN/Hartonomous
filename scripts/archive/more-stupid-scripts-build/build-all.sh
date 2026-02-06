#!/usr/bin/env bash
# ==============================================================================
# Build Everything (C++ + .NET)
# ==============================================================================
# Complete build: C++ engine and .NET solution
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

print_info "=== Building All Components ==="
echo ""

# Build C++ Engine
print_info "[1/3] Building C++ Engine..."
if "$SCRIPT_DIR/build-engine.sh" "$@"; then
    print_success "C++ build complete"
else
    echo "C++ build failed"
    exit 1
fi
echo ""

# Install to local directory
print_info "[2/3] Installing to local install/ directory..."
if "$SCRIPT_DIR/install-local.sh"; then
    print_success "Local installation complete"
else
    echo "Installation failed"
    exit 1
fi
echo ""

# Build .NET
print_info "[3/3] Building .NET Solution..."
cd "$PROJECT_ROOT/app-layer"
if dotnet build -c Release; then
    echo ""
    print_success ".NET build complete"
else
    echo ""
    echo ".NET build failed"
    exit 1
fi

echo ""
print_success "=== Complete Build Successful ==="
echo ""
echo "Libraries:"
echo "  C++:  $PROJECT_ROOT/install/lib/"
echo "  .NET: $PROJECT_ROOT/app-layer/*/bin/Release/net10.0/"
