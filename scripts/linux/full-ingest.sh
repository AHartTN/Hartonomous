#!/bin/bash
# ==============================================================================
# Full Ingestion Pipeline
# ==============================================================================
# Master orchestration script for all ingestion operations
# Follows the full-send.sh pattern of chained script execution
#
# Usage:
#   ./full-ingest.sh [--skip-build] [--testdata-only] [--models-only]
#
# Options:
#   --skip-build    : Skip build step (use existing binaries)
#   --testdata-only : Only ingest test data
#   --models-only   : Only ingest production models
#
# Pipeline Stages:
#   1. Build C++ tools (unless --skip-build)
#   2. Ingest test data models and text files
#   3. Ingest production models from /data/models/hub
# ==============================================================================

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Parse arguments
SKIP_BUILD=false
TESTDATA_ONLY=false
MODELS_ONLY=false

for arg in "$@"; do
    case $arg in
        --skip-build)
            SKIP_BUILD=true
            ;;
        --testdata-only)
            TESTDATA_ONLY=true
            ;;
        --models-only)
            MODELS_ONLY=true
            ;;
    esac
done

# Header
echo ""
echo -e "${CYAN}╔════════════════════════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║                                                            ║${NC}"
echo -e "${CYAN}║          ${BLUE}Hartonomous Full Ingestion Pipeline${CYAN}            ║${NC}"
echo -e "${CYAN}║                                                            ║${NC}"
echo -e "${CYAN}╚════════════════════════════════════════════════════════════╝${NC}"
echo ""

START_TIME=$(date +%s)

# ==============================================================================
# Stage 1: Build (unless skipped)
# ==============================================================================
if [ "$SKIP_BUILD" = false ]; then
    echo -e "${BLUE}[Stage 1/3] Building C++ tools...${NC}"
    "$SCRIPT_DIR/build.sh" -c -T -i > "$PROJECT_ROOT/build-log.txt" 2>&1
    echo -e "${GREEN}✓ Build complete${NC}"
    echo ""
else
    echo -e "${YELLOW}[Stage 1/3] Skipping build (--skip-build)${NC}"
    echo ""
fi

# ==============================================================================
# Stage 2: Test Data Ingestion
# ==============================================================================
if [ "$MODELS_ONLY" = false ]; then
    echo -e "${BLUE}[Stage 2/3] Ingesting test data...${NC}"
    "$SCRIPT_DIR/ingest-all-testdata.sh" > "$PROJECT_ROOT/ingest-testdata-log.txt" 2>&1
    echo -e "${GREEN}✓ Test data ingested${NC}"
    echo ""
else
    echo -e "${YELLOW}[Stage 2/3] Skipping test data (--models-only)${NC}"
    echo ""
fi

# ==============================================================================
# Stage 3: Production Models Ingestion
# ==============================================================================
if [ "$TESTDATA_ONLY" = false ]; then
    echo -e "${BLUE}[Stage 3/3] Ingesting production models...${NC}"
    echo -e "${YELLOW}  This may take a while for large models (Qwen, DeepSeek, etc.)${NC}"
    echo ""

    # Determine parallelism (use half of available cores to avoid thrashing)
    NCORES=$(nproc)
    PARALLEL=$((NCORES / 2))
    if [ $PARALLEL -lt 1 ]; then
        PARALLEL=1
    fi

    "$SCRIPT_DIR/ingest-all-models.sh" --parallel $PARALLEL > "$PROJECT_ROOT/ingest-all-models-log.txt" 2>&1
    echo -e "${GREEN}✓ Production models ingested${NC}"
    echo ""
else
    echo -e "${YELLOW}[Stage 3/3] Skipping production models (--testdata-only)${NC}"
    echo ""
fi

# ==============================================================================
# Summary
# ==============================================================================
END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))
MINUTES=$((ELAPSED / 60))
SECONDS=$((ELAPSED % 60))

echo ""
echo -e "${CYAN}╔════════════════════════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║                                                            ║${NC}"
echo -e "${CYAN}║          ${GREEN}Full Ingestion Pipeline Complete${CYAN}              ║${NC}"
echo -e "${CYAN}║                                                            ║${NC}"
echo -e "${CYAN}╚════════════════════════════════════════════════════════════╝${NC}"
echo ""
echo -e "Time elapsed: ${MINUTES}m ${SECONDS}s"
echo ""
echo "Logs:"
if [ "$SKIP_BUILD" = false ]; then
    echo "  - Build: $PROJECT_ROOT/build-log.txt"
fi
if [ "$MODELS_ONLY" = false ]; then
    echo "  - Test data: $PROJECT_ROOT/ingest-testdata-log.txt"
fi
if [ "$TESTDATA_ONLY" = false ]; then
    echo "  - Production models: $PROJECT_ROOT/ingest-all-models-log.txt"
    echo "  - Individual models: $PROJECT_ROOT/logs/model-ingestion/"
fi
echo ""
