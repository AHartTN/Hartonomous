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

# Ensure we use the locally built library
export LD_LIBRARY_PATH="$(dirname "$0")/../../build/linux-release-max-perf/Engine:$LD_LIBRARY_PATH"

./build/linux-release-max-perf/Engine/tools/ingest_text file "$TEXT_FILE"
