#!/usr/bin/env bash
# Hartonomous Installation Script
# Installs directly from build directory to PostgreSQL system directories.

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

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"

# Check pg_config
if ! command -v pg_config &> /dev/null; then
    print_error "pg_config not found. Install PostgreSQL development files."
    exit 1
fi

PG_LIB_DIR=$(pg_config --pkglibdir)
PG_EXT_DIR=$(pg_config --sharedir)/extension

print_info "=== Installing PostgreSQL Extensions ==="
echo "Build Dir:    $BUILD_DIR"
echo "PG Lib Dir:   $PG_LIB_DIR"
echo "PG Ext Dir:   $PG_EXT_DIR"
echo ""

# Check build directory exists
if [ ! -d "$BUILD_DIR" ]; then
    print_error "Build directory not found: $BUILD_DIR"
    echo "Run ./scripts/linux/01-build.sh first"
    exit 1
fi

# Clean up old installations
print_info "Cleaning old installations..."
sudo rm -f "$PG_LIB_DIR/libengine_core.so"
sudo rm -f "$PG_LIB_DIR/libengine_io.so"
sudo rm -f "$PG_LIB_DIR/libengine.so"
sudo rm -f "$PG_LIB_DIR/s3.so"
sudo rm -f "$PG_LIB_DIR/hartonomous.so"
sudo rm -f "$PG_EXT_DIR/s3.control"
sudo rm -f "$PG_EXT_DIR/s3--0.1.0.sql"
sudo rm -f "$PG_EXT_DIR/hartonomous.control"
sudo rm -f "$PG_EXT_DIR/hartonomous--0.1.0.sql"
# Remove stale copies from /usr/local/lib (ldconfig finds these first)
sudo rm -f /usr/local/lib/libengine_core.so
sudo rm -f /usr/local/lib/libengine_io.so
sudo rm -f /usr/local/lib/libengine.so

# Install Engine libraries to PG lib dir and /usr/local/lib
print_info "Installing Engine libraries..."
for lib in libengine_core.so libengine_io.so libengine.so; do
    if [ -f "$BUILD_DIR/Engine/$lib" ]; then
        sudo install -m 755 "$BUILD_DIR/Engine/$lib" "$PG_LIB_DIR/"
        sudo install -m 755 "$BUILD_DIR/Engine/$lib" "/usr/local/lib/"
        print_success "$lib installed"
    else
        if [ "$lib" = "libengine.so" ]; then
            print_info "$lib not found (optional unified library)"
        else
            print_error "$lib not found in build directory"
            exit 1
        fi
    fi
done
sudo ldconfig

# Install s3 extension
print_info "Installing s3 extension..."
if [ -f "$BUILD_DIR/PostgresExtension/s3/s3.so" ]; then
    sudo install -m 755 "$BUILD_DIR/PostgresExtension/s3/s3.so" "$PG_LIB_DIR/"
    print_success "s3.so installed"
else
    print_error "s3.so not found"
    exit 1
fi

if [ -f "$PROJECT_ROOT/PostgresExtension/s3/s3.control" ]; then
    sudo install -m 644 "$PROJECT_ROOT/PostgresExtension/s3/s3.control" "$PG_EXT_DIR/"
    sudo install -m 644 "$PROJECT_ROOT/PostgresExtension/s3/dist/s3--0.1.0.sql" "$PG_EXT_DIR/"
    print_success "s3 extension files installed"
else
    print_error "s3 extension files not found"
    exit 1
fi

# Install hartonomous extension
print_info "Installing hartonomous extension..."
if [ -f "$BUILD_DIR/PostgresExtension/hartonomous/hartonomous.so" ]; then
    sudo install -m 755 "$BUILD_DIR/PostgresExtension/hartonomous/hartonomous.so" "$PG_LIB_DIR/"
    print_success "hartonomous.so installed"
else
    print_error "hartonomous.so not found"
    exit 1
fi

if [ -f "$PROJECT_ROOT/PostgresExtension/hartonomous/hartonomous.control" ]; then
    sudo install -m 644 "$PROJECT_ROOT/PostgresExtension/hartonomous/hartonomous.control" "$PG_EXT_DIR/"
    sudo install -m 644 "$PROJECT_ROOT/PostgresExtension/hartonomous/dist/hartonomous--0.1.0.sql" "$PG_EXT_DIR/"
    print_success "hartonomous extension files installed"
else
    print_error "hartonomous extension files not found"
    exit 1
fi

print_info "Updating shared library cache..."
sudo ldconfig

echo ""
print_success "=== Installation Complete ==="
