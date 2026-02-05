#!/usr/bin/env bash
# Hartonomous Installation Script
# Uses sudo only for the final copy to system directories.

set -e

# Base directories
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"

# Check pg_config
if ! command -v pg_config &> /dev/null; then
    echo "Error: pg_config not found. PostgreSQL development files must be installed."
    exit 1
fi

PG_LIB_DIR=$(pg_config --pkglibdir)
PG_EXT_DIR=$(pg_config --sharedir)/extension

echo "=== Hartonomous System Installation ==="
echo "Project Root: $PROJECT_ROOT"
echo "Build Dir:    $BUILD_DIR"
echo "PG Lib Dir:   $PG_LIB_DIR"
echo ""

# 0. Clean up old artifacts
echo "Cleaning up old installations..."
sudo rm -f /usr/local/lib/libengine.so
sudo rm -f "$PG_LIB_DIR/libengine.so"

# 1. Install Engine Libraries (Core + IO)
echo "Installing Hartonomous Engine to /usr/local..."

# Function to install lib
install_lib() {
    local lib_name=$1
    if [ -f "$BUILD_DIR/Engine/$lib_name" ]; then
        sudo cp "$BUILD_DIR/Engine/$lib_name" /usr/local/lib/
        sudo cp "$BUILD_DIR/Engine/$lib_name" "$PG_LIB_DIR/"
        echo "✓ $lib_name installed."
    else
        echo "⚠ $lib_name not found in build directory."
    fi
}

install_lib "libengine_core.so"
install_lib "libengine_io.so"

# Headers
sudo mkdir -p /usr/local/include/hartonomous
sudo cp -r "$PROJECT_ROOT/Engine/include/"* /usr/local/include/hartonomous/
echo "✓ Headers copied."

# 2. Install s3 extension
echo "Installing s3 extension..."
S3_SO="$BUILD_DIR/PostgresExtension/s3/s3.so"
S3_SQL="$PROJECT_ROOT/PostgresExtension/s3/dist/s3--0.1.0.sql"
S3_CONTROL="$PROJECT_ROOT/PostgresExtension/s3/s3.control"

if [ -f "$S3_SO" ]; then
    sudo cp "$S3_SO" "$PG_LIB_DIR/"
    echo "✓ s3.so installed."
else
    echo "⚠ s3.so not found at $S3_SO"
fi

if [ -f "$S3_SQL" ] && [ -f "$S3_CONTROL" ]; then
    sudo cp "$S3_SQL" "$PG_EXT_DIR/"
    sudo cp "$S3_CONTROL" "$PG_EXT_DIR/"
    echo "✓ s3 extension SQL/Control files copied."
else
    echo "⚠ s3 extension SQL/Control files missing."
fi

# 3. Install hartonomous extension
echo "Installing hartonomous extension..."
HART_SO="$BUILD_DIR/PostgresExtension/hartonomous/hartonomous.so"
HART_SQL="$PROJECT_ROOT/PostgresExtension/hartonomous/dist/hartonomous--0.1.0.sql"
HART_CONTROL="$PROJECT_ROOT/PostgresExtension/hartonomous/hartonomous.control"

if [ -f "$HART_SO" ]; then
    sudo cp "$HART_SO" "$PG_LIB_DIR/"
    echo "✓ hartonomous.so installed."
else
    echo "⚠ hartonomous.so not found at $HART_SO"
fi

if [ -f "$HART_SQL" ] && [ -f "$HART_CONTROL" ]; then
    sudo cp "$HART_SQL" "$PG_EXT_DIR/"
    sudo cp "$HART_CONTROL" "$PG_EXT_DIR/"
    echo "✓ hartonomous extension SQL/Control files copied."
else
    echo "⚠ hartonomous extension files missing."
fi

echo "Updating shared library cache..."
sudo ldconfig

echo ""
echo "Installation complete."
