#!/bin/bash
# Script to ingest English Wiktionary XML into Hartonomous substrate

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"
INGEST_TOOL="$BUILD_DIR/Engine/tools/ingest_wiktionary_xml"

WIKTIONARY_XML="/data/models/wiktionary/en/enwiktionary-latest-pages-articles.xml"

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Warning: Ingestion tool not found at $INGEST_TOOL."
    echo "Attempting to build it..."
    cmake --build "$BUILD_DIR" --target ingest_wiktionary_xml -j$(nproc)
fi

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Error: Ingestion tool still not found at $INGEST_TOOL."
    exit 1
fi

if [ ! -f "$WIKTIONARY_XML" ]; then
    echo "Error: Wiktionary XML file not found at $WIKTIONARY_XML."
    exit 1
fi

echo "Starting Wiktionary ingestion..."
export LD_LIBRARY_PATH="$BUILD_DIR/Engine:$LD_LIBRARY_PATH"

"$INGEST_TOOL" "$WIKTIONARY_XML"
echo "Wiktionary ingestion complete."
