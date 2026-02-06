#!/usr/bin/env bash
# Script to ingest Unicode Character Database into ucd schema

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"
BUILD_DIR="$PROJECT_ROOT/build/linux-release-max-perf"
INGESTOR="$BUILD_DIR/UCDIngestor/ucd_ingestor"
UCD_DATA="$PROJECT_ROOT/UCDIngestor/data"

if [ ! -f "$INGESTOR" ]; then
    echo "Error: UCD ingestor not found at $INGESTOR. Please build the project first."
    exit 1
fi

if [ ! -d "$UCD_DATA" ]; then
    echo "Error: UCD data directory not found at $UCD_DATA."
    exit 1
fi

# Check for required data files
REQUIRED_FILES=(
    "ucd.all.flat.xml"
    "allkeys.txt"
    "confusables.txt"
    "emoji-sequences.txt"
    "emoji-zwj-sequences.txt"
)

for f in "${REQUIRED_FILES[@]}"; do
    if [ ! -f "$UCD_DATA/$f" ]; then
        echo "Error: Required data file not found: $UCD_DATA/$f"
        exit 1
    fi
done

echo "Starting UCD Data Ingestion..."
echo "This will parse and load Unicode data into ucd.code_points..."

# Set database connection environment variables
export UCD_DB_HOST=${UCD_DB_HOST:-localhost}
export UCD_DB_PORT=${UCD_DB_PORT:-5432}
export UCD_DB_USER=${UCD_DB_USER:-postgres}
export UCD_DB_PASSWORD=${UCD_DB_PASSWORD:-}
export UCD_DB_NAME=${UCD_DB_NAME:-hartonomous}

# Ensure libpqxx can be found
export LD_LIBRARY_PATH="$BUILD_DIR/Engine:$LD_LIBRARY_PATH"

"$INGESTOR" \
    "$UCD_DATA/ucd.all.flat.xml" \
    "$UCD_DATA/allkeys.txt" \
    "$UCD_DATA/confusables.txt" \
    "$UCD_DATA/emoji-sequences.txt" \
    "$UCD_DATA/emoji-zwj-sequences.txt"

echo "✓ UCD Data Ingestion Complete"
