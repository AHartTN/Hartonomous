#!/usr/bin/env bash
# ==============================================================================
# Quick Rebuild Script (Sudo-Free!)
# ==============================================================================
# Use this after setting up dev symlinks with 02-install-dev-symlinks.sh
# Builds and uses cmake --install to populate local install/ directory.
# Symlinks make it visible to PostgreSQL without sudo.
#
# Usage: ./rebuild.sh [--test] [--clean]
# ==============================================================================

set -e

GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠ $1${NC}"; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PRESET="linux-release-max-perf"
BUILD_DIR="$SCRIPT_DIR/build/$PRESET"

# Check if dev symlinks are active
if [ ! -f "$SCRIPT_DIR/.dev-symlinks-active" ]; then
    print_warning "Development symlinks not active!"
    echo ""
    echo "Run this first (one-time setup):"
    echo "  1. ./scripts/linux/01-build.sh"
    echo "  2. cmake --install build/$PRESET --prefix install"
    echo "  3. sudo ./scripts/linux/02-install-dev-symlinks.sh"
    echo ""
    echo "Then you can use this quick rebuild script without sudo"
    exit 1
fi

CLEAN=""
TEST=""

while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--clean) CLEAN="-c"; shift ;;
        -t|--test) TEST="-T"; shift ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

print_info "=== Quick Rebuild (No Sudo!) ==="
echo ""

# Step 1: Build
print_info "[1/3] Building..."
if ./scripts/linux/01-build.sh $CLEAN $TEST; then
    print_success "Build complete"
else
    print_error "Build failed"
    exit 1
fi

# Step 2: Install to local directory via cmake
print_info "[2/3] Installing locally..."
if cmake --install "$BUILD_DIR" --prefix "$SCRIPT_DIR/install" 2>&1; then
    print_success "Local install complete"
else
    print_error "Install failed"
    exit 1
fi

# Step 3: Library cache
echo ""
print_info "[3/3] Checking library cache..."
if [ "${EUID:-$(id -u)}" -eq 0 ]; then
    ldconfig && print_success "Library cache updated"
else
    print_success "Symlinks active - ldconfig not needed"
fi

echo ""
print_success "=== Rebuild Complete ==="
echo ""
echo "Next steps:"
echo "  - Run unit tests: cd build/$PRESET && ctest --output-on-failure -L unit"
echo "  - Setup database: ./scripts/linux/03-setup-database.sh --drop"
echo ""
