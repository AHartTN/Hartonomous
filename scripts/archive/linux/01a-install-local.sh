#!/usr/bin/env bash
# ==============================================================================
# Install to System Directory
# ==============================================================================
# First run: Creates /opt/hartonomous (needs sudo)
# After that: No sudo needed - you own /opt/hartonomous
#
# Usage: 
#   First time: sudo ./scripts/linux/01a-install-local.sh
#   After that: ./scripts/linux/01a-install-local.sh (no sudo!)
# ==============================================================================

set -e

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
RED='\033[0;31m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"
INSTALL_DIR="/opt/hartonomous"

print_info "=== Installing to Local Directory ==="
echo ""
echo "Build Dir:   $BUILD_DIR"
echo "Install Dir: $INSTALL_DIR"
echo ""

# Check build directory exists
if [ ! -d "$BUILD_DIR" ]; then
    print_error "Build directory not found: $BUILD_DIR"
    echo "Run ./scripts/linux/01-build.sh first"
    exit 1
fi

# Create install directory if it doesn't exist
if [ ! -d "$INSTALL_DIR" ]; then
    if [ "$EUID" -ne 0 ]; then
        print_error "First-time setup requires sudo to create $INSTALL_DIR"
        echo "Usage: sudo ./scripts/linux/01a-install-local.sh"
        exit 1
    fi
    print_info "Creating $INSTALL_DIR (first-time setup)"
    mkdir -p "$INSTALL_DIR"
    
    # Get the actual user who invoked sudo
    ACTUAL_USER="${SUDO_USER:-$USER}"
    if [ "$ACTUAL_USER" != "root" ]; then
        chown -R "$ACTUAL_USER:$ACTUAL_USER" "$INSTALL_DIR"
        print_success "Ownership set to $ACTUAL_USER (you can write without sudo from now on)"
    fi
fi

# Run CMake install (no sudo needed if dir already exists and owned by user)
print_info "Running CMake install..."
if cmake --install "$BUILD_DIR" --prefix "$INSTALL_DIR"; then
    print_success "Install complete"
else
    print_error "Install failed"
    exit 1
fi

# Ensure world-readable so postgres can access
chmod -R a+rX "$INSTALL_DIR"
print_success "Permissions set (world-readable)"

echo ""
print_info "Installed artifacts:"
echo ""

# Show what was installed
if [ -d "$INSTALL_DIR/lib" ]; then
    echo "Libraries:"
    ls -lh "$INSTALL_DIR"/lib/*.so 2>/dev/null | awk '{print "  " $9 " (" $5 ")"}'
fi

if [ -d "$INSTALL_DIR/include/hartonomous" ]; then
    echo ""
    echo "Headers:"
    echo "  $INSTALL_DIR/include/hartonomous/ ($(find "$INSTALL_DIR/include/hartonomous" -name '*.h' -o -name '*.hpp' | wc -l) files)"
fi

if [ -d "$INSTALL_DIR/share/postgresql/extension" ]; then
    echo ""
    echo "PostgreSQL Extensions:"
    ls -1 "$INSTALL_DIR"/share/postgresql/extension/* 2>/dev/null | sed 's/^/  /'
fi

echo ""
print_success "=== Local Installation Complete ==="
echo ""
print_info "Your deployment artifacts are in: $INSTALL_DIR"
print_info "No sudo required - you own these files!"
echo ""
