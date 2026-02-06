#!/bin/bash
# ==============================================================================
# All Models Batch Ingestion Script
# ==============================================================================
# Ingests ALL models from /data/models/hub following full-send.sh pattern
#
# Usage:
#   ./ingest-all-models.sh [--parallel N] [--filter PATTERN]
#
# Options:
#   --parallel N    : Process N models in parallel (default: 1)
#   --filter PATTERN: Only ingest models matching pattern (e.g., "Qwen", "DETR")
#
# What it ingests:
#   - All HuggingFace model directories in /data/models/hub
#   - YOLO models (torchscript, ONNX, safetensors)
#   - DETR models
#   - Florence-2 models
#   - Qwen/DeepSeek embedding and code models
# ==============================================================================

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
MODELS_DIR="/data/models/hub"
LOG_DIR="$PROJECT_ROOT/logs/model-ingestion"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Create logs directory
mkdir -p "$LOG_DIR"

# Parse arguments
PARALLEL=1
FILTER_PATTERN=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --parallel)
            PARALLEL="$2"
            shift 2
            ;;
        --filter)
            FILTER_PATTERN="$2"
            shift 2
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

# Header
echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}Hartonomous Model Ingestion (All)${NC}"
echo -e "${BLUE}========================================${NC}"
echo "Models directory: $MODELS_DIR"
echo "Logs directory: $LOG_DIR"
echo "Parallelism: $PARALLEL"
if [ -n "$FILTER_PATTERN" ]; then
    echo "Filter: $FILTER_PATTERN"
fi
echo ""

# Check if models directory exists
if [ ! -d "$MODELS_DIR" ]; then
    echo -e "${RED}Error: Models directory not found: $MODELS_DIR${NC}"
    exit 1
fi

# Find all model directories
MODEL_DIRS=()

# HuggingFace models (models--* pattern)
for model_dir in "$MODELS_DIR"/models--*; do
    if [ -d "$model_dir" ]; then
        if [ -z "$FILTER_PATTERN" ] || [[ "$model_dir" == *"$FILTER_PATTERN"* ]]; then
            MODEL_DIRS+=("$model_dir")
        fi
    fi
done

# Vision models (DETR, Florence, etc.)
for model_dir in "$MODELS_DIR"/*-DETR* "$MODELS_DIR"/Florence-* "$MODELS_DIR"/Grounding-*; do
    if [ -d "$model_dir" ]; then
        if [ -z "$FILTER_PATTERN" ] || [[ "$model_dir" == *"$FILTER_PATTERN"* ]]; then
            MODEL_DIRS+=("$model_dir")
        fi
    fi
done

# YOLO models
if [ -f "/data/models/yolo11x.torchscript" ]; then
    if [ -z "$FILTER_PATTERN" ] || [[ "yolo11x.torchscript" == *"$FILTER_PATTERN"* ]]; then
        MODEL_DIRS+=("/data/models/yolo11x.torchscript")
    fi
fi

# Print summary
echo -e "${GREEN}Found ${#MODEL_DIRS[@]} models to ingest${NC}"
echo ""

# Ingestion function
ingest_model() {
    local model_path="$1"
    local model_name=$(basename "$model_path")
    local log_file="$LOG_DIR/${model_name}.log"

    echo -e "${YELLOW}[STARTED]${NC} $model_name"

    if "$SCRIPT_DIR/ingest-model.sh" "$model_path" > "$log_file" 2>&1; then
        echo -e "${GREEN}[SUCCESS]${NC} $model_name"
        return 0
    else
        echo -e "${RED}[FAILED]${NC} $model_name (see $log_file)"
        return 1
    fi
}

export -f ingest_model
export SCRIPT_DIR LOG_DIR RED GREEN YELLOW NC

# Run ingestion
START_TIME=$(date +%s)

if [ "$PARALLEL" -eq 1 ]; then
    # Sequential ingestion
    SUCCESS=0
    FAILED=0

    for model_path in "${MODEL_DIRS[@]}"; do
        if ingest_model "$model_path"; then
            ((SUCCESS++))
        else
            ((FAILED++))
        fi
    done
else
    # Parallel ingestion using GNU parallel or xargs
    if command -v parallel &> /dev/null; then
        printf '%s\n' "${MODEL_DIRS[@]}" | parallel -j "$PARALLEL" ingest_model
    else
        printf '%s\n' "${MODEL_DIRS[@]}" | xargs -P "$PARALLEL" -I {} bash -c 'ingest_model "$@"' _ {}
    fi

    # Count results
    SUCCESS=$(grep -l "\[SUCCESS\]" "$LOG_DIR"/*.log | wc -l)
    FAILED=$(grep -l "\[FAILED\]" "$LOG_DIR"/*.log | wc -l)
fi

END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))

# Summary
echo ""
echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}Batch Ingestion Complete${NC}"
echo -e "${BLUE}========================================${NC}"
echo -e "${GREEN}Success: $SUCCESS${NC}"
echo -e "${RED}Failed: $FAILED${NC}"
echo "Time elapsed: ${ELAPSED}s"
echo "Logs: $LOG_DIR"
echo ""
