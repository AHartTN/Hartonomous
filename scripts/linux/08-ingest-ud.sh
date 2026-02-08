#!/bin/bash
# Script to ingest Universal Dependencies (CoNLL-U) treebanks into Hartonomous substrate

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"
INGEST_TOOL="$BUILD_DIR/Engine/tools/ingest_ud"

UD_DIR="/data/models/ud-treebanks/ud-treebanks-v2.17"

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Warning: Ingestion tool not found at $INGEST_TOOL."
    echo "Attempting to build it..."
    cmake --build "$BUILD_DIR" --target ingest_ud -j$(nproc)
fi

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Error: Ingestion tool still not found at $INGEST_TOOL. Please build the project first."
    exit 1
fi

if [ ! -d "$UD_DIR" ]; then
    echo "Error: UD treebanks directory not found at $UD_DIR."
    exit 1
fi

echo "Starting Universal Dependencies ingestion..."
export LD_LIBRARY_PATH="$BUILD_DIR/Engine:$LD_LIBRARY_PATH"

"$INGEST_TOOL" "$UD_DIR"
echo "Universal Dependencies ingestion complete."
