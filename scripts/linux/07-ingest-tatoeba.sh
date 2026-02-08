#!/bin/bash
# Script to ingest Tatoeba translation-linked sentences into Hartonomous substrate

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"
INGEST_TOOL="$BUILD_DIR/Engine/tools/ingest_tatoeba"

SENTENCES_CSV="/data/models/tatoeba/sentences.csv"
LINKS_CSV="/data/models/tatoeba/links.csv"

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Warning: Ingestion tool not found at $INGEST_TOOL."
    echo "Attempting to build it..."
    cmake --build "$BUILD_DIR" --target ingest_tatoeba -j$(nproc)
fi

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Error: Ingestion tool still not found at $INGEST_TOOL. Please build the project first."
    exit 1
fi

if [ ! -f "$SENTENCES_CSV" ]; then
    echo "Error: Tatoeba sentences file not found at $SENTENCES_CSV."
    exit 1
fi

if [ ! -f "$LINKS_CSV" ]; then
    echo "Error: Tatoeba links file not found at $LINKS_CSV."
    exit 1
fi

echo "Starting Tatoeba ingestion..."
export LD_LIBRARY_PATH="$BUILD_DIR/Engine:$LD_LIBRARY_PATH"

"$INGEST_TOOL" "$SENTENCES_CSV" "$LINKS_CSV"
echo "Tatoeba ingestion complete."
