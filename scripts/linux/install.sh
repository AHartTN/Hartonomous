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
echo ""

# 1. Install Engine Library and Headers
echo "Installing Hartonomous Engine to /usr/local..."
if [ -f "$BUILD_DIR/Engine/libengine.so" ]; then
    sudo cp "$BUILD_DIR/Engine/libengine.so" /usr/local/lib/
    sudo mkdir -p /usr/local/include/hartonomous
    sudo cp -r "$PROJECT_ROOT/Engine/include/"* /usr/local/include/hartonomous/
    echo "✓ Engine library and headers copied."
else
    echo "⚠ libengine.so not found. Skipping engine install."
fi

# 2. Install Engine library to PG lib directory (for extension resolution)
echo "Installing libengine.so to PostgreSQL..."
if [ -f "$BUILD_DIR/Engine/libengine.so" ]; then
    sudo cp "$BUILD_DIR/Engine/libengine.so" "$PG_LIB_DIR/"
    echo "✓ libengine.so copied to $PG_LIB_DIR"
fi

# 3. Install s3 extension
echo "Installing s3 extension..."
S3_SO="$PROJECT_ROOT/PostgresExtension/s3/s3.so"
S3_SQL="$PROJECT_ROOT/PostgresExtension/s3/dist/s3--0.1.0.sql"
S3_CONTROL="$PROJECT_ROOT/PostgresExtension/s3/s3.control"

if [ -f "$S3_SO" ] && [ -f "$S3_SQL" ]; then
    sudo cp "$S3_SO" "$PG_LIB_DIR/"
    sudo cp "$S3_SQL" "$PG_EXT_DIR/"
    sudo cp "$S3_CONTROL" "$PG_EXT_DIR/"
    echo "✓ s3 extension files copied."
else
    echo "⚠ s3 extension files not found. Ensure you built them."
fi

# 4. Install hartonomous extension
echo "Installing hartonomous extension..."
HART_SQL="$PROJECT_ROOT/PostgresExtension/hartonomous/dist/hartonomous--0.1.0.sql"
HART_CONTROL="$PROJECT_ROOT/PostgresExtension/hartonomous/hartonomous.control"

if [ -f "$HART_SQL" ] && [ -f "$HART_CONTROL" ]; then
    sudo cp "$HART_SQL" "$PG_EXT_DIR/"
    sudo cp "$HART_CONTROL" "$PG_EXT_DIR/"
    echo "✓ hartonomous extension files copied."
else
    echo "⚠ hartonomous extension files not found."
fi

echo "Updating shared library cache..."
sudo ldconfig

echo ""
echo "Installation complete."
