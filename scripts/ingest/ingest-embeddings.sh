#!/usr/bin/env bash
# ==============================================================================
# Ingest Embedding Model (Extract Semantic Edges)
# ==============================================================================
# Ingests transformer embedding model (e.g., MiniLM 30k×384)
# 
# Process:
#   1. Load embedding weights from model directory
#   2. Use HNSWLib for approximate nearest neighbor search
#   3. Extract semantic edges (which tokens are geometrically proximate in embedding space)
#   4. Store edges as Relationships in hartonomous database
#
# This bridges transformer knowledge into the geometric intelligence substrate.
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$SCRIPT_DIR/../lib/common.sh"

INGEST_MODEL="$PROJECT_ROOT/build/linux-release-max-perf/Engine/tools/ingest_model"

# Default to MiniLM model
MODEL_DIR="${1:-$PROJECT_ROOT/test-data/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2/snapshots}"

# Find snapshot directory  
if [ -d "$MODEL_DIR" ]; then
    SNAPSHOT=$(ls "$MODEL_DIR" | head -n 1)
    if [ -n "$SNAPSHOT" ]; then
        MODEL_DIR="$MODEL_DIR/$SNAPSHOT"
    fi
fi

# Environment for hartonomous database
export HARTONOMOUS_DB_HOST="${HARTONOMOUS_DB_HOST:-localhost}"
export HARTONOMOUS_DB_PORT="${HARTONOMOUS_DB_PORT:-5432}"
export HARTONOMOUS_DB_USER="${HARTONOMOUS_DB_USER:-postgres}"
export HARTONOMOUS_DB_NAME="hartonomous"

print_header "Ingest Embedding Model - Bridge Transformer Knowledge"

# Verify tool
if [ ! -f "$INGEST_MODEL" ]; then
    print_error "ingest_model tool not found: $INGEST_MODEL"
    echo "Build the engine first: ./scripts/build/build-engine.sh"
    exit 1
fi

# Verify model directory
if [ ! -d "$MODEL_DIR" ]; then
    print_error "Model directory not found: $MODEL_DIR"
    echo ""
    echo "Usage: $0 [model_directory]"
    echo "Example: $0 test-data/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2/snapshots/<hash>"
    exit 1
fi

print_step "Ingesting model from: $MODEL_DIR"
print_info "Extracting semantic edges using HNSWLib"
echo ""

if "$INGEST_MODEL" "$MODEL_DIR"; then
    print_success "Embedding model ingested"
    print_complete "Transformer knowledge bridged into geometric substrate"
else
    print_error "Embedding ingestion failed"
    exit 1
fi
