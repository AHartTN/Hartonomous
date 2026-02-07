#!/bin/bash
# Ingest Production Models from /data/models/hub
# This extracts semantic relationships from the weights of foundation models.

# Environment
source $(dirname "$0")/00_env.sh

# Path to the Ingestion Tool
INGEST_TOOL="./build/linux-release-max-perf/Engine/tools/ingest_model"

if [ ! -f "$INGEST_TOOL" ]; then
    echo "Error: Ingestion tool not found at $INGEST_TOOL"
    exit 1
fi

# Function to ingest a specific model path
ingest_path() {
    local model_path="$1"
    local description="$2"

    if [ -d "$model_path" ]; then
        echo "----------------------------------------------------------------"
        echo "Ingesting $description"
        echo "Path: $model_path"
        echo "----------------------------------------------------------------"
        # Determine if it's a standard HF snapshot or a flat directory
        local target_dir="$model_path"
        if [ -d "$model_path/snapshots" ]; then
            # Use the most recent snapshot
            target_dir="$model_path/snapshots/$(ls -1 "$model_path/snapshots" | head -n 1)"
        fi
        
        $INGEST_TOOL "$target_dir"
    else
        echo "Skipping $description: Path not found ($model_path)"
    fi
}

# 1. Embeddings (The backbone of semantic search)
ingest_path "/data/models/hub/models--sentence-transformers--all-MiniLM-L6-v2" "MiniLM (Baseline Embeddings)"
ingest_path "/data/models/hub/models--Qwen--Qwen3-Embedding-4B" "Qwen3 4B (High-Dim Embeddings)"

# 2. Vision / Multimodal (Spatial/Visual grounding)
ingest_path "/data/models/hub/Florence-2-large" "Florence-2 Large (Vision-Language)"
ingest_path "/data/models/hub/Grounding-DINO-Base" "Grounding DINO (Object Detection)"

# 3. Code / Logic (Procedural reasoning)
ingest_path "/data/models/hub/models--Qwen--Qwen2.5-Coder-7B-Instruct" "Qwen2.5 Coder 7B"

echo "----------------------------------------------------------------"
echo "All model ingestion tasks completed."
echo "----------------------------------------------------------------"
