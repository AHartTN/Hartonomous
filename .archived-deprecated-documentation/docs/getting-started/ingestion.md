# Model Ingestion

Hartonomous supports ingesting models in multiple formats with full atomization and spatial indexing.

## GGUF Models (Ollama/llama.cpp)

Ingest quantized GGUF models from Ollama or other sources:

```bash
# Full model ingestion
python scripts/ingest_model.py "D:/Models/blobs/sha256-1194..." --name "Qwen3-30B"

# Test with first 10 tensors
python scripts/ingest_model.py path/to/model.gguf --name "TestModel" --max-tensors 10

# Custom sparsity threshold
python scripts/ingest_model.py model.gguf --name "Model" --threshold 0.01
```

## SafeTensors Models (Hugging Face)

Ingest SafeTensors format models with optional config and tokenizer:

```bash
# Full model with config and tokenizer
python scripts/ingest_safetensors.py \
    .cache/embedding_models/all-MiniLM-L6-v2/model.safetensors \
    --name "all-MiniLM-L6-v2" \
    --config .cache/embedding_models/all-MiniLM-L6-v2/config.json \
    --tokenizer .cache/embedding_models/all-MiniLM-L6-v2/tokenizer.json

# Just model weights
python scripts/ingest_safetensors.py model.safetensors --name "MyModel"

# Test with first 5 tensors
python scripts/ingest_safetensors.py model.safetensors --name "Test" --max-tensors 5
```

## What Gets Atomized

Both formats extract and atomize:

### 1. **Vocabulary/Tokenizer** (Phase 1)
- Each token becomes an atom with semantic positioning
- 384D embeddings → 3D via PCA (all-MiniLM-L6-v2)
- 6.7x semantic clustering validated
- Cross-model token deduplication

### 2. **Architecture/Config** (Phase 2)
- Hyperparameters as geometric constraints
- Layer counts, attention heads, hidden dimensions
- Cross-model architecture sharing

### 3. **Tensor Weights** (Phase 3)
- Sparse encoding (threshold: 1e-6 default)
- RLE compression for repeated values
- Batch deduplication (100-200x faster)
- GPU acceleration (CuPy) when available

## Example Results

From test run (2 tensors, 590K weights):
```
Tensors Processed: 2
Total Weights: 589,952
Atoms Created: 344
Deduplication: 1715x
Sparse: 0.2%
```

## Validation Test

Quick pipeline validation:

```bash
# Uses smallest GGUF, processes 2 tensors only
python scripts/test_model_ingestion.py
```

## Performance

- **Vocabulary**: ~2000 tokens/s with semantic embeddings
- **Weights**: 100-200x faster with batch atomization
- **Deduplication**: 1000-2000x typical for quantized weights
- **GPU Acceleration**: 5-10x speedup with CuPy (optional)

## Storage Efficiency

Example 30B parameter model:
- Raw: ~60GB (FP16)
- Quantized: ~18GB (Q4_K_M)
- Atomized: ~3-5GB (sparse + dedup + compression)
- **Savings**: 85-95% vs quantized

## Next Steps

After ingestion:
1. **Query atoms**: Spatial proximity searches
2. **Model transformations**: Merge, prune, quantize
3. **Inference**: CPU-first execution via atoms
4. **Code generation**: From atomic patterns
