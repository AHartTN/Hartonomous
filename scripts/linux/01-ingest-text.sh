#!/bin/bash
# Ingest Moby Dick text into substrate

# Environment
source $(dirname "$0")/00_env.sh

TEXT_FILE="test-data/moby_dick.txt"

if [ ! -f "$TEXT_FILE" ]; then
    echo "Error: $TEXT_FILE not found"
    exit 1
fi

echo "Ingesting text from: $TEXT_FILE"

./build/linux-release-max-perf/Engine/tools/ingest_text "$TEXT_FILE"
