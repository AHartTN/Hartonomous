#!/usr/bin/env bash
# ==============================================================================
# Hartonomous Development Setup - Symlink Installation
# ==============================================================================
# One-time setup to replace sudo-installed files with symlinks to install directory
# After this, you can rebuild without sudo - only ldconfig needs sudo
#
# Prerequisites:
#   1. Build: ./scripts/linux/01-build.sh
#   2. Install locally: ./scripts/linux/01a-install-local.sh
#   3. Then run this (one time): sudo ./scripts/linux/02-install-dev-symlinks.sh
#
# After setup, just rebuild and run:
#   ./rebuild.sh (handles build + install + ldconfig)
# ==============================================================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠ $1${NC}"; }

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    print_error "This script must be run with sudo (one-time setup)"
    echo "After setup, you won't need sudo for rebuilds (only ldconfig)"
    exit 1
fi

# Get paths
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
INSTALL_DIR="$PROJECT_ROOT/install"

# Check install directory exists
if [ ! -d "$INSTALL_DIR/lib" ]; then
    print_error "Install directory not found: $INSTALL_DIR/lib"
    echo ""
    echo "Run these first:"
    echo "  1. ./scripts/linux/01-build.sh"
    echo "  2. ./scripts/linux/01a-install-local.sh"
    exit 1
fi

# Check pg_config
if ! command -v pg_config &> /dev/null; then
    print_error "pg_config not found"
    exit 1
fi

PG_LIB_DIR=$(pg_config --pkglibdir)
PG_EXT_DIR=$(pg_config --sharedir)/extension

print_info "=== Hartonomous Development Setup (Symlinks) ==="
echo ""
echo "Project Root: $PROJECT_ROOT"
echo "Install Dir:  $INSTALL_DIR"
echo "PG Lib Dir:   $PG_LIB_DIR"
echo "PG Ext Dir:   $PG_EXT_DIR"
echo ""

print_warning "This will remove existing installations and replace with symlinks"
print_info "Symlinks will point to: $INSTALL_DIR"
print_info "(Survives clean builds - install/ directory is permanent)"
echo ""
read -p "Continue? (y/N) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    print_info "Aborted"
    exit 0
fi

# ==============================================================================
# STEP 1: Remove existing installations
# ==============================================================================
print_info "Step 1: Removing existing installations..."

# /usr/local/lib
rm -f /usr/local/lib/libengine.so
rm -f /usr/local/lib/libengine_core.so
rm -f /usr/local/lib/libengine_io.so
print_success "Removed /usr/local/lib/libengine*"

# PostgreSQL lib directory
rm -f "$PG_LIB_DIR/libengine.so"
rm -f "$PG_LIB_DIR/libengine_core.so"
rm -f "$PG_LIB_DIR/libengine_io.so"
rm -f "$PG_LIB_DIR/s3.so"
rm -f "$PG_LIB_DIR/hartonomous.so"
print_success "Removed $PG_LIB_DIR extensions"

# PostgreSQL extension files (these can stay as copies - they don't change often)
# But we'll symlink them anyway for consistency
rm -f "$PG_EXT_DIR/s3.control"
rm -f "$PG_EXT_DIR/s3--*.sql"
rm -f "$PG_EXT_DIR/hartonomous.control"
rm -f "$PG_EXT_DIR/hartonomous--*.sql"
print_success "Removed $PG_EXT_DIR extension configs"

# Headers (symlink entire directory)
rm -rf /usr/local/include/hartonomous
print_success "Removed /usr/local/include/hartonomous"

# ==============================================================================
# STEP 2: Create symlinks to install directory
# ==============================================================================
print_info "Step 2: Creating symlinks to install directory..."

# Helper function
create_symlink() {
    local target=$1
    local link_name=$2
    local description=$3
    
    if [ ! -e "$target" ]; then
        print_warning "$description: Source doesn't exist: $target"
        print_info "  (Run ./scripts/linux/01a-install-local.sh first)"
        return
    fi
    
    mkdir -p "$(dirname "$link_name")"
    ln -sf "$target" "$link_name"
    print_success "$description: $link_name -> $target"
}

# Engine libraries to /usr/local/lib
create_symlink "$INSTALL_DIR/lib/libengine_core.so" "/usr/local/lib/libengine_core.so" "libengine_core.so"
create_symlink "$INSTALL_DIR/lib/libengine_io.so" "/usr/local/lib/libengine_io.so" "libengine_io.so"
create_symlink "$INSTALL_DIR/lib/libengine.so" "/usr/local/lib/libengine.so" "libengine.so (unified)"

# Engine libraries to PostgreSQL lib dir (PostgreSQL needs them too)
create_symlink "$INSTALL_DIR/lib/libengine_core.so" "$PG_LIB_DIR/libengine_core.so" "PG libengine_core.so"
create_symlink "$INSTALL_DIR/lib/libengine_io.so" "$PG_LIB_DIR/libengine_io.so" "PG libengine_io.so"
create_symlink "$INSTALL_DIR/lib/libengine.so" "$PG_LIB_DIR/libengine.so" "PG libengine.so"

# PostgreSQL extensions
create_symlink "$INSTALL_DIR/lib/s3.so" "$PG_LIB_DIR/s3.so" "s3.so"
create_symlink "$INSTALL_DIR/lib/hartonomous.so" "$PG_LIB_DIR/hartonomous.so" "hartonomous.so"

# Extension SQL/Control files
create_symlink "$INSTALL_DIR/share/postgresql/extension/s3.control" "$PG_EXT_DIR/s3.control" "s3.control"
create_symlink "$INSTALL_DIR/share/postgresql/extension/s3--0.1.0.sql" "$PG_EXT_DIR/s3--0.1.0.sql" "s3--0.1.0.sql"
create_symlink "$INSTALL_DIR/share/postgresql/extension/hartonomous.control" "$PG_EXT_DIR/hartonomous.control" "hartonomous.control"
create_symlink "$INSTALL_DIR/share/postgresql/extension/hartonomous--0.1.0.sql" "$PG_EXT_DIR/hartonomous--0.1.0.sql" "hartonomous--0.1.0.sql"

# Headers (symlink entire directory)
create_symlink "$INSTALL_DIR/include/hartonomous" "/usr/local/include/hartonomous" "Headers"

# ==============================================================================
# STEP 3: Update library cache
# ==============================================================================
print_info "Step 3: Updating library cache..."
ldconfig
print_success "Library cache updated"

# ==============================================================================
# SUCCESS
# ==============================================================================
echo ""
print_success "=== Development Setup Complete ==="
echo ""
print_info "Symlinks created! Now you can develop without sudo:"
echo ""
echo "  1. Make code changes"
echo "  2. Rebuild: ./rebuild.sh (handles everything!)"
echo "  3. Test your changes"
echo ""
print_info "The symlinks point to your install/ directory, which rebuilding"
print_info "automatically updates. Clean builds won't break symlinks!"
echo ""
print_warning "If you delete the install/ directory, run this script again"
echo ""

# Create a helpful reminder file
cat > "$PROJECT_ROOT/.dev-symlinks-active" << 'EOF'
# Hartonomous Development Mode Active

This file indicates that development symlinks are active.

## Quick Workflow:
1. Edit code
2. Rebuild: ./rebuild.sh (build + install + ldconfig)
3. Test

## Symlinks Point To:
./install/ directory (permanent, survives clean builds)

## Restore to Normal Installation:
sudo ./scripts/linux/02-install.sh

## See Symlinks:
ls -lh /usr/local/lib/libengine*
ls -lh $(pg_config --pkglibdir)/{s3,hartonomous,libengine}*
EOF

print_info "Created .dev-symlinks-active marker file"
echo ""
