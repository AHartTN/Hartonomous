# Test Suite Organization

## Quick Start

**Dev tests (fast, small models):**
```bash
# Test GGUF ingestion (~637MB TinyLlama)
python scripts/test_model_ingestion.py

# Test SafeTensors ingestion (~87MB embedding model)
python scripts/test_safetensors_ingestion.py

# Run all unit tests
pytest tests/unit -m unit

# Run all tests
pytest
```

**Production ingestion (D:\Models, 18GB+):**
```bash
# Ingest GGUF from Ollama storage
python scripts/ingest_model.py "D:/Models/blobs/sha256-1194..." --name "Qwen3-30B"

# Ingest SafeTensors from file
python scripts/ingest_safetensors.py model.safetensors --name "Model" --config config.json
```

## Test Structure

```
scripts/
  test_model_ingestion.py          # Dev: GGUF ingestion (~637MB)
  test_safetensors_ingestion.py    # Dev: SafeTensors ingestion (~87MB)
  ingest_model.py                  # Prod: GGUF CLI (D:\Models, 18GB+)
  ingest_safetensors.py            # Prod: SafeTensors CLI

tests/
  conftest.py                      # Shared fixtures
  unit/                            # Fast unit tests
    test_base_atomizer.py
    test_compression.py
    test_hilbert_curve.py
    test_landmark_projection.py
  integration/                     # Integration tests (require DB)
    test_db_connection.py
    test_text_parser.py
    test_structured_parser.py
  sql/                             # SQL function tests
    test_atom_composition.py
    test_atomization_functions.py
    test_spatial_functions.py
    test_hilbert_sql.py
  functional/                      # End-to-end tests

api/tests/                         # API-specific tests
```

## Test Models

**Development (cached, gitignored):**
- GGUF: `.cache/test_models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf` (~637MB)
- SafeTensors: `.cache/embedding_models/all-MiniLM-L6-v2/snapshots/.../model.safetensors` (~87MB)

**Production (D:\Models):**
- `sha256-1194...` = 17,697 MB (smallest)
- `sha256-6be6...` = 62,341 MB
- `sha256-9d50...` = 64,313 MB
- `sha256-7724...` = 276,622 MB

## Pytest Markers

```bash
# Run only fast unit tests
pytest -m unit

# Run integration tests (requires DB)
pytest -m integration

# Run GGUF tests
pytest -m gguf

# Run SafeTensors tests
pytest -m safetensors

# Run SQL tests
pytest -m sql

# Exclude slow tests
pytest -m "not slow"
```

## CI/CD

CI runs:
1. Unit tests (fast, no DB)
2. Integration tests (with test DB)
3. SQL tests (with test DB)

CI does NOT run:
- Model ingestion tests (models too large)
- Production scripts (require D:\Models)

## Cleaning Up

Root-level test files to be moved to `tests/` directory:
- `test_positioning_functional.py` → `tests/functional/`
- `test_local_connection.py` → `tests/integration/`
- `test_implementation.py` → `tests/functional/`
- `test_hilbert_functional.py` → `tests/functional/`
- `test_gguf_tokens.py` → `tests/integration/` (mark as `@pytest.mark.gguf`)
- `test_gguf_ingestion.py` → `tests/integration/` (mark as `@pytest.mark.gguf`)
- `test_compression_functional.py` → `tests/functional/`
- `test_all_imports.py` → `tests/unit/`

## Next Steps

1. ✅ Test scripts use small cached models
2. ✅ Production scripts use D:\Models
3. ✅ pytest.ini configured
4. ✅ conftest.py has test fixtures
5. ⏭️ Move root-level test files to tests/
6. ⏭️ Add pytest markers to tests
7. ⏭️ Create tests/functional/ directory
