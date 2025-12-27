#!/usr/bin/env bash
# Hartonomous Build Script
# Usage: ./build.sh [--clean] [--test] [--release] [--ingest <path>]
# Idempotent, CI/CD compatible

set -e
cd "$(dirname "$0")"

CLEAN=0
TEST=0
INGEST_PATH=""
BUILD_TYPE="debug"

while [[ $# -gt 0 ]]; do
    case $1 in
        --clean) CLEAN=1; shift ;;
        --test) TEST=1; shift ;;
        --release) BUILD_TYPE="release"; shift ;;
        --ingest) INGEST_PATH="$2"; shift 2 ;;
        *) shift ;;
    esac
done

# Determine preset based on OS
if [[ "$OSTYPE" == "darwin"* ]]; then
    CONFIG_PRESET="macos-clang-$BUILD_TYPE"
elif [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "mingw"* ]] || [[ -n "$WINDIR" ]]; then
    CONFIG_PRESET="windows-clang-$BUILD_TYPE"
else
    CONFIG_PRESET="linux-clang-$BUILD_TYPE"
fi

BUILD_DIR="out/build/$CONFIG_PRESET"

# Clean
if [ $CLEAN -eq 1 ] && [ -d "$BUILD_DIR" ]; then
    echo "Cleaning $BUILD_DIR..."
    rm -rf "$BUILD_DIR"
fi

# Configure (idempotent)
if [ ! -f "$BUILD_DIR/build.ninja" ]; then
    echo "Configuring with preset: $CONFIG_PRESET"
    cmake --preset "$CONFIG_PRESET"
fi

# Build
echo "Building with preset: $CONFIG_PRESET"
cmake --build --preset "$CONFIG_PRESET" --parallel

# Test
if [ $TEST -eq 1 ]; then
    echo "Testing with preset: $CONFIG_PRESET"
    ctest --preset "$CONFIG_PRESET" --output-on-failure
    exit $?
fi

# Ingest
if [ -n "$INGEST_PATH" ]; then
    echo "Ingesting..."
    ./"$BUILD_DIR"/bin/hartonomous-ingest "$INGEST_PATH"
    exit $?
fi

echo "Done."
echo "  --test           Run tests"
echo "  --ingest <path>  Ingest file or directory"
