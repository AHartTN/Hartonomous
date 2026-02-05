# Hartonomous Ingestion Scripts

## Overview

This document describes the ingestion scripts following the `full-send.sh` chaining pattern.

---

## 📁 Script Directory Structure

```
scripts/linux/
├── full-send.sh              # Original: build → setup DB → seed Unicode
├── full-ingest.sh            # NEW: orchestrates all ingestion
├── ingest-model.sh           # NEW: single model package
├── ingest-text.sh            # NEW: single text file
├── ingest-all-testdata.sh    # NEW: all test data
└── ingest-all-models.sh      # NEW: all production models
```

---

## 🚀 Quick Start

### Ingest Everything (Test Data + Production Models)

```bash
./scripts/linux/full-ingest.sh
```

This runs:
1. Build C++ tools
2. Ingest test data (simple models + text files)
3. Ingest production models (DETR, Qwen, DeepSeek, etc.)

**Options:**
```bash
./scripts/linux/full-ingest.sh --skip-build        # Use existing binaries
./scripts/linux/full-ingest.sh --testdata-only     # Skip production models
./scripts/linux/full-ingest.sh --models-only       # Skip test data
```

### Ingest Single Model

```bash
./scripts/linux/ingest-model.sh /data/models/hub/models--Qwen--Qwen3-Embedding-4B
```

**What it does:**
- Parses directory structure → Merkle DAG
- Extracts vocabulary → Compositions
- Applies spectral analysis to embeddings → 4D S³ positions
- Ingests all tensors → graph structure with Relations
- Converts attention weights → Relations with ELO ratings

**Supports:**
- HuggingFace model directories (models--*)
- SafeTensor files (.safetensors)
- PyTorch models (.pth)
- ONNX models (.onnx)
- TorchScript models (.torchscript)

### Ingest Single Text File

```bash
./scripts/linux/ingest-text.sh /data/models/test_data/text/moby_dick.txt
```

**What it does:**
- Extracts n-grams (1-4) → Compositions
- Content-addresses via BLAKE3
- Links to existing model vocabulary Physicality

**Supports:**
- Plain text (.txt)
- Code files (.py, .cpp, .js, etc.)
- JSON (.json)
- Any UTF-8 text

### Ingest All Test Data

```bash
./scripts/linux/ingest-all-testdata.sh
```

**What it ingests:**
- `simple_cnn.safetensors` (small test model)
- `all-MiniLM-L6-v2` (sentence transformer)
- All `.txt` files in `test_data/text/`
- All code files (`.py`, `.json`)

**Options:**
```bash
./scripts/linux/ingest-all-testdata.sh --models-only   # Skip text files
./scripts/linux/ingest-all-testdata.sh --text-only     # Skip models
```

### Ingest All Production Models

```bash
./scripts/linux/ingest-all-models.sh
```

**What it ingests:**
- All HuggingFace models in `/data/models/hub/models--*`
- Vision models: DETR, Florence-2, Grounding-DINO
- Code models: DeepSeek, Qwen
- Audio models: SAM-Audio, Fish-Speech, Granite-Speech
- Embedding models: Qwen3-Embedding, Qwen3-Reranker
- YOLO: `yolo11x.torchscript`

**Options:**
```bash
./scripts/linux/ingest-all-models.sh --parallel 4       # Process 4 models in parallel
./scripts/linux/ingest-all-models.sh --filter Qwen      # Only ingest Qwen models
./scripts/linux/ingest-all-models.sh --filter DETR      # Only ingest DETR models
```

**Performance:**
- Sequential: Processes one model at a time
- Parallel: Uses GNU `parallel` or `xargs -P` to process N models concurrently
- Default parallelism: 1 (sequential)

---

## 📊 Expected Performance

### Small Models (< 1GB)
- Example: `simple_cnn.safetensors`, `all-MiniLM-L6-v2`
- Time: < 30 seconds
- CPU: 100% utilization during spectral decomposition

### Medium Models (1-10GB)
- Example: `Qwen2.5-Coder-7B`, `Florence-2-large`, `DETR-ResNet-101`
- Time: 2-10 minutes
- CPU: Parallelized affinity matrix computation (OpenMP)

### Large Models (> 10GB)
- Example: `DeepSeek-Coder-33B`, `Qwen3-Embedding-4B`
- Time: 15 minutes - 2 hours
- CPU: Multi-threaded throughout (OpenMP + MKL)

**Bottlenecks:**
1. **Spectral Decomposition**: O(n² k) for k-NN affinity matrix (parallelized with OpenMP)
2. **Eigenvalue Solver**: O(nk²) for k eigenvectors (MKL-accelerated)
3. **Database Insertion**: Bulk COPY is already optimal

**Optimization Tip:**
For large vocabularies (> 100K tokens), consider implementing approximate k-NN via HNSWLib (see `docs/OPTIMIZATION_REVIEW.md`).

---

## 📂 Log Files

All scripts follow the `full-send.sh` pattern of redirecting output to log files:

### Single Model
```bash
./scripts/linux/ingest-model.sh <model_path>
```
Output: Printed to console (pipe to file if needed)

### Batch Ingestion
```bash
./scripts/linux/full-ingest.sh
```
Logs:
- `build-log.txt` - Build output
- `ingest-testdata-log.txt` - Test data ingestion summary
- `ingest-all-models-log.txt` - Production models summary
- `logs/model-ingestion/<model_name>.log` - Individual model logs

### Example Log Structure
```
Hartonomous/
├── build-log.txt
├── ingest-testdata-log.txt
├── ingest-all-models-log.txt
└── logs/
    └── model-ingestion/
        ├── models--Qwen--Qwen3-Embedding-4B.log
        ├── models--deepseek-ai--deepseek-coder-33b-instruct.log
        ├── DETR-ResNet-101.log
        └── yolo11x.torchscript.log
```

---

## 🔧 Environment Variables

### OpenMP Threads
Scripts automatically set `OMP_NUM_THREADS=$(nproc)` for full CPU utilization.

**Manual override:**
```bash
export OMP_NUM_THREADS=8  # Use 8 threads
./scripts/linux/ingest-model.sh <model_path>
```

### PostgreSQL Connection
Tools use libpq environment variables:
```bash
export PGHOST=localhost
export PGPORT=5432
export PGDATABASE=hartonomous
export PGUSER=postgres
export PGPASSWORD=yourpassword
```

Or use `.pgpass` file:
```bash
echo "localhost:5432:hartonomous:postgres:yourpassword" > ~/.pgpass
chmod 600 ~/.pgpass
```

---

## 🧪 Testing

### Verify Build
```bash
./scripts/linux/build.sh -c -T -i > build-log.txt 2>&1
ls -lh build/tools/ingest_model
ls -lh build/tools/ingest_text
```

### Test Single Model (Fast)
```bash
./scripts/linux/ingest-model.sh /data/models/test_data/simple_cnn.safetensors
```

Expected output:
```
========================================
Model Package Ingestion
========================================
Model: /data/models/test_data/simple_cnn.safetensors
Tool: /home/ahart/Projects/Hartonomous/build/tools/ingest_model
OpenMP threads: 16

[1/5] Parsing model package...
[2/5] Ingesting JSON configs...
[3/5] Extracting vocabulary...
[4/5] Spectral decomposition...
  [1/5] Building k-NN affinity matrix...
  [2/5] Computing graph Laplacian...
  [3/5] Extracting top-4 eigenvectors...
  [4/5] Gram-Schmidt orthonormalization...
  [5/5] Normalizing to S³ hypersphere...
  ✓ Spectral decomposition complete
[5/5] Ingesting tensor weights...

=== Ingestion Complete ===
  Configurations: 0
  Vocabulary tokens: 0
  Compositions created: 0
  Physicality records: 0
  Relations created: 0
  Tensors processed: 2
  Weight atoms: 15234
  Deduplicated: 8932

========================================
Ingestion Complete
========================================
Time elapsed: 12s
```

### Test Text Ingestion
```bash
./scripts/linux/ingest-text.sh /data/models/test_data/text/moby_dick.txt
```

Expected output:
```
========================================
Text Ingestion
========================================
File: /data/models/test_data/text/moby_dick.txt
Tool: /home/ahart/Projects/Hartonomous/build/tools/ingest_text

Processing text file...
  Total characters: 1215235
  N-grams extracted: 245892
  Compositions created: 189234
  Linked to existing vocab: 87432

========================================
Ingestion Complete
========================================
Time elapsed: 8s
```

---

## 🐛 Troubleshooting

### Error: `ingest_model tool not found`
**Solution:**
```bash
./scripts/linux/build.sh -c -T -i > build-log.txt 2>&1
```

### Error: `Failed to connect to database`
**Solution:**
Check PostgreSQL connection:
```bash
psql -h localhost -U postgres -d hartonomous -c "\dt hartonomous.*"
```

Verify environment variables:
```bash
echo $PGHOST $PGPORT $PGDATABASE $PGUSER
```

### Error: `relation "hartonomous.lexicon" does not exist`
**Solution:**
Extension needs rebuilding:
```bash
cd PostgresExtension/hartonomous
make
sudo make install
psql -d hartonomous -c "DROP EXTENSION IF EXISTS hartonomous CASCADE; CREATE EXTENSION hartonomous;"
```

### Warning: `simple_cnn.safetensors ingestion failed`
**Cause:** May not have proper structure (vocab, embeddings)

**Solution:** Check logs:
```bash
cat logs/ingest-simple-cnn.log
```

Small test models without embeddings will skip spectral decomposition (expected behavior).

---

## 📚 Examples

### Example 1: Ingest Qwen3 Embedding Model
```bash
./scripts/linux/ingest-model.sh /data/models/hub/models--Qwen--Qwen3-Embedding-4B
```

**What happens:**
1. Loads `tokenizer.json` → 151,936 tokens
2. Extracts embedding matrix (151936 × 4096)
3. Spectral decomposition (4096D → 4D S³) via Laplacian Eigenmaps
4. Creates 151,936 Physicality records with Hilbert indices
5. Links Compositions to Physicality
6. Ingests 2 safetensor files (model-00001-of-00002, model-00002-of-00002)
7. Extracts attention weights → Relations with ELO

**Time:** ~5-10 minutes (depends on CPU)

### Example 2: Ingest All DETR Models
```bash
./scripts/linux/ingest-all-models.sh --filter DETR
```

**What happens:**
1. Finds: `DETR-ResNet-101`, `Conditional-DETR-R50`
2. Ingests each sequentially
3. Creates separate logs for each model

### Example 3: Parallel Ingestion of All Models
```bash
./scripts/linux/ingest-all-models.sh --parallel 4
```

**What happens:**
1. Processes 4 models concurrently
2. Each model gets its own log file
3. Summary shows success/failure counts

**Warning:** Ensure sufficient RAM (each model needs 2-8GB during ingestion)

### Example 4: Ingest Moby Dick
```bash
./scripts/linux/ingest-text.sh /data/models/test_data/text/moby_dick.txt
```

**What happens:**
1. Extracts n-grams: "Call", "me", "Ishmael", "Call me", "me Ishmael", "Call me Ishmael"
2. Creates Composition for each unique n-gram
3. Links to existing model vocabulary (if available)
4. Stores n-gram sequences via RelationSequence

---

## 🎯 Best Practices

### 1. Start Small
```bash
# First, ingest test data to verify everything works
./scripts/linux/ingest-all-testdata.sh

# Then, ingest a single production model
./scripts/linux/ingest-model.sh /data/models/hub/Florence-2-base

# Finally, ingest all models
./scripts/linux/ingest-all-models.sh --parallel 2
```

### 2. Monitor Logs
```bash
# Tail logs in real-time
tail -f logs/model-ingestion/models--Qwen--Qwen3-Embedding-4B.log
```

### 3. Database Vacuum After Ingestion
```bash
psql -d hartonomous -c "VACUUM ANALYZE hartonomous.compositions;"
psql -d hartonomous -c "VACUUM ANALYZE hartonomous.physicality;"
psql -d hartonomous -c "VACUUM ANALYZE hartonomous.relations;"
```

### 4. Check Disk Space
```bash
# Models + database can consume significant space
df -h /data
du -sh /var/lib/postgresql/*/main
```

---

## 📖 Related Documentation

- `docs/OPTIMIZATION_REVIEW.md` - Performance optimization analysis
- `scripts/linux/full-send.sh` - Original orchestration pattern
- `Engine/include/ingestion/model_ingester.hpp` - Model ingestion interface
- `Engine/include/ml/spectral_analysis.hpp` - Spectral decomposition interface
