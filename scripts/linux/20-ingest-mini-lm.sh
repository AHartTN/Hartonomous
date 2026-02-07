#!/bin/bash
# Ingest MiniLM embedding model into substrate

# Environment
source $(dirname "$0")/00_env.sh

MODEL_DIR="test-data/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2/snapshots/$(ls test-data/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2/snapshots/)"

echo "Ingesting model from: $MODEL_DIR"

"$BUILD_DIR/Engine/tools/ingest_model" "$MODEL_DIR"
