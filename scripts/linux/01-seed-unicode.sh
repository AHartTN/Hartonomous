#!/usr/bin/env bash
# Script to run the optimized Unicode seeding tool

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"
SEED_TOOL="$BUILD_DIR/Engine/tools/seed_unicode"
UCD_DATA="$PROJECT_ROOT/UCDIngestor/data"

if [ ! -f "$SEED_TOOL" ]; then
    echo "Error: Seeding tool not found at $SEED_TOOL. Please build the project first."
    exit 1
fi

if [ ! -d "$UCD_DATA" ]; then
    echo "Error: UCD data directory not found at $UCD_DATA."
    exit 1
fi

echo "Starting Unicode Seeding..."
# Ensure libengine.so can be found
export LD_LIBRARY_PATH="$BUILD_DIR/Engine:$LD_LIBRARY_PATH"

"$SEED_TOOL" "$UCD_DATA"
