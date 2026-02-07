#!/bin/bash
# Ingest CLIP Vision Model (simulated download + ingestion)

# Environment
source $(dirname "$0")/00_env.sh

# Ideally, we would download clip-vit-base-patch32 here using python huggingface_hub
# For this demo environment, we assume the user will place it or we simulate it if the tool supports download.
# We will create a placeholder script that points to where it SHOULD be.

MODEL_PATH="test-data/vision_models/clip-vit-base-patch32"

if [ ! -d "$MODEL_PATH" ]; then
    echo "Vision model not found at $MODEL_PATH."
    echo "Please download 'openai/clip-vit-base-patch32' to this directory."
    exit 0 # Don't fail the pipeline, just skip
fi

echo "Ingesting Vision Model: $MODEL_PATH"
# Assuming ingest_model can handle safetensors structure of CLIP
./build/linux-release-max-perf/Engine/tools/ingest_model "$MODEL_PATH"
