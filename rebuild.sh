#!/usr/bin/env bash
# ==============================================================================
# Quick Rebuild Script (Sudo-Free!)
# ==============================================================================
# Use this after setting up dev symlinks with 02-install-dev-symlinks.sh
# Builds, installs to local install/ directory, and the symlinks make it work!
# No sudo needed - ldconfig isn't required when symlinks are active.
#
# Usage: ./rebuild.sh [--test] [--clean]
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

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Check if dev symlinks are active
if [ ! -f "$SCRIPT_DIR/.dev-symlinks-active" ]; then
    print_warning "Development symlinks not active!"
    echo ""
    echo "Run this first (one-time setup):"
    echo "  1. ./scripts/linux/01-build.sh"
    echo "  2. ./scripts/linux/01a-install-local.sh"
    echo "  3. sudo ./scripts/linux/02-install-dev-symlinks.sh"
    echo ""
    echo "Then you can use this quick rebuild script without sudo"
    exit 1
fi

# Parse arguments
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
    echo ""
    print_warning "Build failed - check output above"
    exit 1
fi

# Step 2: Install to local directory
print_info "[2/3] Installing locally..."
if ./scripts/linux/01a-install-local.sh; then
    print_success "Local install complete"
else
    echo ""
    print_warning "Install failed - check output above"
    exit 1
fi

# Step 3: Update library cache (optional with symlinks)
echo ""
print_info "[3/3] Checking library cache..."
# With symlinks in place, ldconfig usually isn't needed for each rebuild
# The symlinks already point to the updated files in install/
if [ "${EUID:-$(id -u)}" -eq 0 ]; then
    # Already running as root, just run it
    if ldconfig; then
        print_success "Library cache updated"
    fi
else
    # Not root - skip ldconfig, symlinks handle it
    print_success "Symlinks active - ldconfig not needed"
    echo "  (If you encounter library loading issues, run: sudo ldconfig)"
fi

echo ""
print_success "=== Rebuild Complete (No Sudo Required!) ==="
echo ""
print_info "Symlinks automatically use updated files from install/!"
echo ""
echo "Next steps:"
echo "  - Restart services that use the libraries"
echo "  - Run tests: cd build/linux-release-max-perf/Engine/tests && ./suite_test_*"
echo "  - Setup database: ./scripts/linux/03-setup-database.sh --drop"
echo ""
