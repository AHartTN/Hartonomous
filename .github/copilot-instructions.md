# Hartonomous Agent Instructions

## What This Is

Hartonomous is a **geometric intelligence substrate** — a reinvention of AI where meaning emerges from relationships between geometrically-positioned entities, not from learned weights. This is NOT a database, NOT RAG, NOT vector search. Intelligence = navigation through ELO-weighted relationship space on a 4D hypersphere (S³).

## Build & Test

```bash
# Build everything (75 targets: 3 libs, 2 extensions, 4 tools, 7 test binaries)
cmake --preset linux-release-max-perf
cmake --build build/linux-release-max-perf -j$(nproc)

# Run unit tests (20 tests, all pass)
cd build/linux-release-max-perf
LD_LIBRARY_PATH="$PWD/Engine:$LD_LIBRARY_PATH" ctest --output-on-failure -L unit

# Full pipeline (build → install → db → seed → ingest → test)
./full-send.sh

# Quick dev rebuild (requires one-time symlink setup)
./rebuild.sh --clean --test
```

**MKL required:** If cmake can't find MKL, run `source /opt/intel/oneapi/setvars.sh` first.

## Architecture

```
PostgreSQL (hartonomous database)
  ├── s3.so         → libengine_core.so  (S³ geometry: distance, GiST index, interpolation)
  └── hartonomous.so → libengine_io.so   (ingestion, query, graph navigation)
                       └── libengine.so  (unified for .NET P/Invoke)

Engine/   (C++20, Intel MKL, 33 .cpp + 41 .hpp)
  ├── src/geometry/     Super Fibonacci S³ distribution, Hopf fibration, geodesic distance
  ├── src/hashing/      BLAKE3 content-addressing (128-bit, deterministic)
  ├── src/spatial/      Hilbert curve 4D (locality-preserving 4D→1D index)
  ├── src/unicode/ingestor/  UCD/UCA parser → semantic sequencing → S³ projection
  ├── src/ingestion/    Text→Atoms→Compositions→Relations pipeline
  ├── src/cognitive/    WalkEngine (graph nav), GödelEngine (gap detection), OODA loop
  ├── src/storage/      AtomStore, CompositionStore, RelationStore, EvidenceStore
  ├── src/database/     PostgresConnection, BulkCopy
  ├── src/query/        SemanticQuery, AIOperations
  ├── src/ml/           HNSWLib nearest-neighbor, model extraction
  ├── tools/            seed_unicode, ingest_text, ingest_model, walk_test
  └── data/ucd/         Unicode Character Database source files (gitignored, 549MB)

PostgresExtension/
  ├── s3/              4D S³ geometry: geodesic distance, GiST KNN, <=> operator
  └── hartonomous/     C shim: blake3_hash(), codepoint_to_s3(), ingest_text()

scripts/
  ├── linux/           Pipeline: 01-build, 02-install, 03-setup-database, 05-seed-unicode, etc.
  ├── sql/             Schema: 00-foundation, 01-core-tables, 02-functions
  └── lib/common.sh    Shared utilities
```

## The Three Layers

1. **Atoms** — 1,114,112 Unicode codepoints, each positioned on S³ via semantic sequencing (category→script→UCA weight→radical/strokes). Seeded once, immutable. Position determines clustering: 'a' near 'A' near 'ä'.

2. **Compositions** — N-grams of atoms (words/tokens). Content-addressed via BLAKE3. Centroid = average of atom S³ positions. Run-length encoded.

3. **Relations** — Co-occurrence patterns between compositions. **This is where intelligence lives.** ELO-rated from aggregated evidence across text, models, and feedback. Every relation traceable to its source via RelationEvidence.

## Rules for Implementation

### Layer Discipline
- **Math/geometry** → `Engine/src/geometry/`, `Engine/src/spatial/`
- **Business logic** → `Engine/src/cognitive/`, `Engine/src/query/`
- **Storage** → `Engine/src/storage/`
- **SQL wrappers** → `PostgresExtension/hartonomous/src/` (pure C shim)
- **API** → `app-layer/Hartonomous.API/`

### Never Do This
- **Don't implement logic in SQL.** SQL is a thin wrapper. Graph traversal, ELO updates, reasoning — all in C++ engine. SQL calls `hartonomous.so` → `libengine.so` → C++.
- **Don't link against object libraries.** `engine_core_objs` is OBJECT, not linkable. Use `engine_core`, `engine_io`, or `engine`.
- **Don't add tools with manual include paths.** Link `engine_io` — it transitively provides everything.
- **Don't bypass semantic sequencing for atoms.** Atom S³ positions come from UCD/UCA semantic ordering, NOT hash-based projection. AtomLookup retrieves pre-seeded positions.

### Always Do This
- **Verify before assuming.** `grep -r "function_name" Engine/src/` to check if something exists.
- **Test after changes.** `cd build/linux-release-max-perf && ctest --output-on-failure -L unit`
- **Use C++20.** Modern idioms, `std::optional`, structured bindings, concepts where appropriate.
- **Keep interop_api.h in sync** with `NativeBindings.cs` when adding C exports.

## Key Files

| Purpose | File |
|---------|------|
| C interop API (23 functions) | `Engine/include/interop_api.h` |
| S³ distribution | `Engine/src/geometry/super_fibonacci.cpp` |
| Text ingestion pipeline | `Engine/src/ingestion/text_ingester.cpp` |
| Atom seeding (UCD→S³) | `Engine/src/unicode/ingestor/ucd_processor.cpp` |
| Graph navigation | `Engine/src/cognitive/walk_engine.cpp` |
| Database schema | `scripts/sql/01-core-tables.sql` |
| S³ extension SQL | `PostgresExtension/s3/dist/s3--0.1.0.sql` |
| Vision document | `docs/VISION.md` |

## Pipeline Order

1. Build (`01-build.sh`) — CMake + Ninja, preset `linux-release-max-perf`
2. Install extensions (`02-install.sh`) — copies .so + .sql to PostgreSQL dirs (sudo)
3. Setup database (`03-setup-database.sh --drop`) — creates DB, loads schema
4. Seed atoms (`05-seed-unicode.sh`) — 1.114M codepoints from UCD data → `hartonomous.atom`
5. Ingest models (`20-ingest-mini-lm.sh`) — extract relationships from embeddings
6. Ingest text (`30-ingest-text.sh`) — text → compositions → relations
7. Query/walk (`40-run-queries.sh`) — semantic queries, graph traversal