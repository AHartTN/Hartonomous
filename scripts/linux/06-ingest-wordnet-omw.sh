#!/bin/bash
# Script to ingest Princeton WordNet and OMW-data into Hartonomous substrate

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"
INGEST_TOOL="$BUILD_DIR/Engine/tools/ingest_wordnet_omw"

WORDNET_DIR="/data/models/princeton-wordnet/WordNet-3.0/dict"
OMW_DATA_DIR="$PROJECT_ROOT/Engine/external/omw-data"

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Warning: Ingestion tool not found at $INGEST_TOOL."
    echo "Attempting to build it..."
    cmake --build "$BUILD_DIR" --target ingest_wordnet_omw -j$(nproc)
fi

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Error: Ingestion tool still not found at $INGEST_TOOL. Please build the project first."
    exit 1
fi

if [ ! -d "$WORDNET_DIR" ]; then
    echo "Error: WordNet dictionary directory not found at $WORDNET_DIR."
    exit 1
fi

if [ ! -d "$OMW_DATA_DIR" ]; then
    echo "Error: OMW data directory not found at $OMW_DATA_DIR."
    exit 1
fi

echo "Starting WordNet/OMW ingestion..."
# Ensure libengine.so can be found
export LD_LIBRARY_PATH="$BUILD_DIR/Engine:$LD_LIBRARY_PATH"

"$INGEST_TOOL" "$WORDNET_DIR" "$OMW_DATA_DIR"
echo "WordNet/OMW ingestion complete."
