#!/bin/bash
# ==============================================================================
# Test Data Batch Ingestion Script
# ==============================================================================
# Orchestrates ingestion of all test data following full-send.sh pattern
#
# Usage:
#   ./ingest-all-testdata.sh [--models-only|--text-only]
#
# What it does:
#   1. Ingests small test models (simple_cnn.safetensors)
#   2. Ingests all text files from test_data/text/
#   3. (Future) Ingests images and audio
# ==============================================================================

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TEST_DATA_DIR="/data/models/test_data"
LOG_DIR="$PROJECT_ROOT/logs"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Create logs directory
mkdir -p "$LOG_DIR"

# Parse arguments
MODELS_ONLY=false
TEXT_ONLY=false

for arg in "$@"; do
    case $arg in
        --models-only)
            MODELS_ONLY=true
            ;;
        --text-only)
            TEXT_ONLY=true
            ;;
    esac
done

# Header
echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}Hartonomous Test Data Ingestion${NC}"
echo -e "${BLUE}========================================${NC}"
echo "Test Data: $TEST_DATA_DIR"
echo "Logs: $LOG_DIR"
echo ""

# ==============================================================================
# 1. Ingest Test Models
# ==============================================================================
if [ "$TEXT_ONLY" = false ]; then
    echo -e "${GREEN}[1/2] Ingesting test models...${NC}"

    # Simple CNN safetensors
    if [ -f "$TEST_DATA_DIR/simple_cnn.safetensors" ]; then
        echo "  → simple_cnn.safetensors"
        "$SCRIPT_DIR/ingest-model.sh" "$TEST_DATA_DIR/simple_cnn.safetensors" > "$LOG_DIR/ingest-simple-cnn.log" 2>&1 || {
            echo -e "${YELLOW}  ⚠ Warning: simple_cnn.safetensors ingestion failed (see logs)${NC}"
        }
    fi

    # all-MiniLM-L6-v2 (via symlink in neural/)
    if [ -L "$TEST_DATA_DIR/neural/all-MiniLM-L6-v2" ]; then
        MINI_LM_PATH=$(readlink -f "$TEST_DATA_DIR/neural/all-MiniLM-L6-v2")
        if [ -d "$MINI_LM_PATH" ]; then
            echo "  → all-MiniLM-L6-v2"
            "$SCRIPT_DIR/ingest-model.sh" "$MINI_LM_PATH" > "$LOG_DIR/ingest-all-minilm-l6-v2.log" 2>&1 || {
                echo -e "${YELLOW}  ⚠ Warning: all-MiniLM-L6-v2 ingestion failed (see logs)${NC}"
            }
        fi
    fi

    echo ""
fi

# ==============================================================================
# 2. Ingest Text Files
# ==============================================================================
if [ "$MODELS_ONLY" = false ]; then
    echo -e "${GREEN}[2/2] Ingesting text files...${NC}"

    if [ -d "$TEST_DATA_DIR/text" ]; then
        for text_file in "$TEST_DATA_DIR/text"/*.txt "$TEST_DATA_DIR/text"/*.py "$TEST_DATA_DIR/text"/*.json; do
            if [ -f "$text_file" ]; then
                filename=$(basename "$text_file")
                echo "  → $filename"
                log_name=$(echo "$filename" | sed 's/\.[^.]*$//')
                "$SCRIPT_DIR/ingest-text.sh" "$text_file" > "$LOG_DIR/ingest-text-$log_name.log" 2>&1 || {
                    echo -e "${YELLOW}  ⚠ Warning: $filename ingestion failed (see logs)${NC}"
                }
            fi
        done
    else
        echo -e "${YELLOW}  ⚠ Text directory not found: $TEST_DATA_DIR/text${NC}"
    fi

    echo ""
fi

# ==============================================================================
# Summary
# ==============================================================================
echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}Batch Ingestion Complete${NC}"
echo -e "${BLUE}========================================${NC}"
echo "Check logs in: $LOG_DIR"
echo ""
