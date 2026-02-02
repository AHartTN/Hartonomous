#!/bin/bash
# ==============================================================================
# Model Package Ingestion Script
# ==============================================================================
# Ingests complete AI model packages (HuggingFace, safetensors, etc.)
#
# Usage:
#   ./ingest-model.sh <model_directory>
#   ./ingest-model.sh /data/models/hub/models--Qwen--Qwen3-Embedding-4B
#
# What it ingests:
#   - Directory structure → Merkle DAG
#   - Vocabulary (tokenizer.json, vocab.txt) → Compositions
#   - Embeddings → 4D S³ positions via spectral decomposition
#   - All tensors → graph structure with Relations
#   - Attention weights → Relations with ELO ratings
# ==============================================================================

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/build"
TOOL_PATH="$BUILD_DIR/tools/ingest_model"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check arguments
if [ $# -lt 1 ]; then
    echo -e "${RED}Error: Missing model directory argument${NC}"
    echo ""
    echo "Usage: $0 <model_directory>"
    echo ""
    echo "Examples:"
    echo "  $0 /data/models/hub/models--Qwen--Qwen3-Embedding-4B"
    echo "  $0 /data/models/hub/Florence-2-large"
    echo "  $0 /data/models/test_data/simple_cnn.safetensors"
    exit 1
fi

MODEL_DIR="$1"

# Validate directory exists
if [ ! -e "$MODEL_DIR" ]; then
    echo -e "${RED}Error: Model directory does not exist: $MODEL_DIR${NC}"
    exit 1
fi

# Check if tool exists
if [ ! -f "$TOOL_PATH" ]; then
    echo -e "${RED}Error: ingest_model tool not found at $TOOL_PATH${NC}"
    echo -e "${YELLOW}Run ./scripts/linux/build.sh first${NC}"
    exit 1
fi

# Run ingestion
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Model Package Ingestion${NC}"
echo -e "${GREEN}========================================${NC}"
echo "Model: $MODEL_DIR"
echo "Tool: $TOOL_PATH"
echo ""

# Set OpenMP threads to use all available cores
export OMP_NUM_THREADS=$(nproc)
echo "OpenMP threads: $OMP_NUM_THREADS"
echo ""

# Run with timing
START_TIME=$(date +%s)

"$TOOL_PATH" "$MODEL_DIR"

END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))

echo ""
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Ingestion Complete${NC}"
echo -e "${GREEN}========================================${NC}"
echo "Time elapsed: ${ELAPSED}s"
echo ""
