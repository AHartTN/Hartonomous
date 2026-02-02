#!/bin/bash
# ==============================================================================
# Text Ingestion Script
# ==============================================================================
# Ingests text files into the Hartonomous substrate
#
# Usage:
#   ./ingest-text.sh <text_file>
#   ./ingest-text.sh /data/models/test_data/text/moby_dick.txt
#
# What it ingests:
#   - Text → n-grams (1-4) → Compositions
#   - Each n-gram gets content-addressed via BLAKE3
#   - Compositions are linked to Physicality via existing model vocab
# ==============================================================================

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/build"
TOOL_PATH="$BUILD_DIR/tools/ingest_text"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check arguments
if [ $# -lt 1 ]; then
    echo -e "${RED}Error: Missing text file argument${NC}"
    echo ""
    echo "Usage: $0 <text_file>"
    echo ""
    echo "Examples:"
    echo "  $0 /data/models/test_data/text/moby_dick.txt"
    echo "  $0 /data/models/test_data/text/sample.txt"
    echo "  $0 /data/models/test_data/text/code.py"
    exit 1
fi

TEXT_FILE="$1"

# Validate file exists
if [ ! -f "$TEXT_FILE" ]; then
    echo -e "${RED}Error: Text file does not exist: $TEXT_FILE${NC}"
    exit 1
fi

# Check if tool exists
if [ ! -f "$TOOL_PATH" ]; then
    echo -e "${RED}Error: ingest_text tool not found at $TOOL_PATH${NC}"
    echo -e "${YELLOW}Run ./scripts/linux/build.sh first${NC}"
    exit 1
fi

# Run ingestion
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Text Ingestion${NC}"
echo -e "${GREEN}========================================${NC}"
echo "File: $TEXT_FILE"
echo "Tool: $TOOL_PATH"
echo ""

# Run with timing
START_TIME=$(date +%s)

"$TOOL_PATH" "$TEXT_FILE"

END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))

echo ""
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Ingestion Complete${NC}"
echo -e "${GREEN}========================================${NC}"
echo "Time elapsed: ${ELAPSED}s"
echo ""
