# Implementation Priority - High Velocity Session

## THIS SESSION - Functional Wins Only

### Phase 1: Core Ingestion (60 min)
**Goal:** Ingest text and get it in database

1. ✅ **BLAKE3 Wrapper** (15 min)
   - `Engine/src/hashing/blake3_pipeline.cpp`
   - Simple wrapper, batch support
   - Test: Hash "Hello World"

2. ✅ **Database Connection** (15 min)
   - `Engine/src/database/postgres_connection.cpp`
   - libpq wrapper
   - Connection pooling (basic)

3. ✅ **Text Ingestion** (30 min)
   - `Engine/src/ingestion/text_ingester.cpp`
   - Decompose text → atoms, compositions
   - Store in PostgreSQL
   - Test: Ingest "Call me Ishmael"
   - Test: Ingest Moby Dick (full text)

### Phase 2: Safetensor Ingestion (45 min)
**Goal:** Load HuggingFace models and extract data

4. ✅ **Safetensor Loader** (30 min)
   - `Engine/src/ingestion/safetensor_loader.cpp`
   - Parse safetensor format
   - Extract tensors, config, tokenizer
   - Store embeddings as compositions

5. ✅ **Model Extraction** (15 min)
   - Extract attention weights (if available)
   - Store as semantic_edges with ELO
   - Test: Load minilm from test-data/

### Phase 3: Query Engine (30 min)
**Goal:** Query and get answers

6. ✅ **Basic Query** (30 min)
   - `Engine/src/query/semantic_query.cpp`
   - Find compositions by text
   - Find relations containing composition
   - Find co-occurring compositions
   - Test: "What is the captain's name?" → "Ahab"

### Phase 4: Postgres Extension (30 min)
**Goal:** Enable custom SQL functions

7. ✅ **Extension Framework** (15 min)
   - `PostgresExtension/src/hartonomous.c`
   - Basic extension registration
   - Version function

8. ✅ **Custom Functions** (15 min)
   - `ingest_text(TEXT)` function
   - `semantic_query(TEXT)` function
   - Calls C++ from C wrapper

### Phase 5: Automation Scripts (30 min)
**Goal:** One command to do everything

9. ✅ **Individual Scripts** (15 min)
   - `scripts/01-rebuild-database.sh` - Drop/recreate hypercube
   - `scripts/02-build-all.sh` - Build C++ + extension
   - `scripts/03-install-extension.sh` - Copy libs, install extension
   - `scripts/04-ingest-test-data.sh` - Ingest Moby Dick + minilm
   - `scripts/05-run-queries.sh` - Test queries

10. ✅ **Master Script** (15 min)
    - `scripts/00-full-pipeline.sh` - Run all steps
    - With checkpoints and error handling

### Phase 6: Verification (15 min)

11. ✅ **End-to-End Test**
    - Run master script
    - Verify ingestion worked
    - Verify queries return correct results
    - Test with multiple models

---

## Total Time: ~3.5 hours for MVP

## Deferred (Not This Session)
- ❌ Images/Video/Audio ingestion (text + safetensors first)
- ❌ Visualization
- ❌ Advanced cognitive architecture
- ❌ C# wrapper
- ❌ Comprehensive benchmarks
- ❌ Advanced query features (Tree of Thought, etc.)

---

## Success Criteria

### Must Have (This Session):
- ✅ Text ingestion working (Moby Dick)
- ✅ Safetensor ingestion working (minilm)
- ✅ Query returning "Ahab" for "captain"
- ✅ One script to run entire pipeline
- ✅ Postgres extension installed and working

### Nice to Have:
- ⚠️ Multiple model support
- ⚠️ Compression metrics (90%+)
- ⚠️ Performance profiling

---

## File Creation Order

1. `Engine/include/hashing/blake3_pipeline.hpp`
2. `Engine/src/hashing/blake3_pipeline.cpp`
3. `Engine/include/database/postgres_connection.hpp`
4. `Engine/src/database/postgres_connection.cpp`
5. `Engine/include/ingestion/text_ingester.hpp`
6. `Engine/src/ingestion/text_ingester.cpp`
7. `Engine/include/ingestion/safetensor_loader.hpp`
8. `Engine/src/ingestion/safetensor_loader.cpp`
9. `Engine/include/query/semantic_query.hpp`
10. `Engine/src/query/semantic_query.cpp`
11. `PostgresExtension/src/hartonomous.c`
12. `scripts/01-rebuild-database.sh`
13. `scripts/02-build-all.sh`
14. `scripts/03-install-extension.sh`
15. `scripts/04-ingest-test-data.sh`
16. `scripts/05-run-queries.sh`
17. `scripts/00-full-pipeline.sh`

---

## Rapid Development Guidelines

- Keep it simple, make it work
- No premature optimization
- Hardcode where needed (environment vars later)
- Copy-paste is okay for speed
- Error handling: assert + stderr
- Test after each component
- Commit often

Let's build!
