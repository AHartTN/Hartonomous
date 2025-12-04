# Enterprise Test Suite Refactoring Plan

**Date**: November 30, 2025  
**Goal**: Transform ad-hoc test scripts into production-ready, enterprise-grade test architecture aligned with geometric greenfield system

## Current State Analysis

### Test Structure (48 test files)
```
tests/
├── geometric/        4 files ✅ GREENFIELD (44/44 tests passing)
├── integration/     15 files ⚠️  MIXED (brownfield + greenfield)
├── unit/             6 files ⚠️  MIXED (some brownfield utilities)
├── functional/       3 files ⚠️  MIXED (compression, hilbert, positioning)
├── performance/      3 files ❌ BROWNFIELD (uses legacy atomization)
├── sql/              6 files ⚠️  MIXED (some test deprecated tables)
├── smoke/            3 files ✅ GREENFIELD (validation only)
└── conftest.py      ✅ Good fixtures, needs .cache model support
```

### Coverage Status

**STRONG (Greenfield Architecture):**
- ✅ **tests/geometric/** (100% Phase 1-3)
  - `test_geometric_atomization.py`: AtomLocator, TrajectoryBuilder, SpatialReconstructor, GeometricAtomizer
  - `test_fractal_atomization.py`: FractalAtomizer, BPECrystallizer, deduplication, compression
  - `test_fractal_edge_cases.py`: Edge cases, performance benchmarks
  - `test_bpe_integration.py`: OODA loop, autonomous learning, BPE phases
  - **Result**: 44/44 tests passing, 318K ops/sec, 769x compression

**WEAK (Missing/Incomplete):**
- ❌ **Real model ingestion**: test_fractal_ingestion_pipeline.py hangs, uses legacy GGUFAtomizer
- ❌ **Performance benchmarks**: test_model_ingestion.py uses brownfield, no geometric version
- ❌ **SafeTensors support**: test_safetensors_ingestion.py uses legacy, no geometric pipeline
- ❌ **Integration tests**: Many use CompositionBuilder instead of TrajectoryBuilder
- ❌ **Code atomizer tests**: test_code_atomizer_integration.py has 2 failing tests (metadata TypeError)

**BROWNFIELD (Legacy Architecture):**
- ❌ `tests/integration/test_gguf_atomizer.py`: Tests CompositionBuilder
- ❌ `tests/integration/test_tensor_reconstruction.py`: Uses TensorAtomizer (legacy)
- ❌ `tests/performance/test_model_ingestion.py`: GGUFAtomizer with CompositionBuilder
- ❌ `tests/performance/test_safetensors_ingestion.py`: Legacy atomization
- ⚠️  `tests/sql/test_atom_composition.py`: Tests deprecated 002_atom_composition table

### Available Test Assets

**Models in `.cache/`:**
- ✅ `test_models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf` (~637MB) - Quantized GGUF
- ✅ `embedding_models/all-MiniLM-L6-v2/` (~87MB SafeTensors) - Embedding model

**Conftest Fixtures:**
```python
db_connection       # Session-scoped DB connection
clean_db            # Truncates atom, composition, relation tables
test_gguf_path      # Path to TinyLlama GGUF
test_safetensors_path  # Path to MiniLM SafeTensors
project_root        # Repository root
```

## Enterprise Test Architecture

### Phase 1: Core Unit Tests (Greenfield Only)

**Location**: `tests/unit/geometric/`

**Structure**:
```
tests/unit/geometric/
├── test_atom_locator.py          # AtomLocator coordinate mapping
├── test_trajectory_builder.py    # TrajectoryBuilder LINESTRING creation
├── test_spatial_reconstructor.py # WKT parsing, reconstruction
├── test_fractal_atomizer.py      # FractalAtomizer deduplication
├── test_bpe_crystallizer.py      # BPECrystallizer autonomous learning
└── test_geometric_pipeline.py    # End-to-end unit pipeline
```

**Coverage Target**: 100% of geometric atomization code  
**Performance Target**: < 1 second total execution  
**Dependencies**: None (pure unit tests, mocked DB)

**Migrations**:
- Move `tests/geometric/test_geometric_atomization.py` → split into focused unit tests
- Move `tests/geometric/test_fractal_atomization.py` → `test_fractal_atomizer.py`
- Move `tests/geometric/test_fractal_edge_cases.py` → `test_bpe_crystallizer.py` + `test_edge_cases.py`
- Move `tests/geometric/test_bpe_integration.py` → `test_bpe_crystallizer.py`

### Phase 2: Integration Tests (Real DB + Models)

**Location**: `tests/integration/geometric/`

**Structure**:
```
tests/integration/geometric/
├── test_gguf_ingestion.py        # TinyLlama GGUF → geometric atomization
├── test_safetensors_ingestion.py # MiniLM SafeTensors → geometric atomization
├── test_spatial_queries.py       # PostGIS queries on trajectories
├── test_fractal_reconstruction.py # Bit-perfect tensor reconstruction
├── test_compression_ratios.py    # Verify 769x compression claim
└── test_end_to_end_workflow.py   # Full ingest → query → reconstruct
```

**Coverage Target**: 100% of real model pipelines  
**Performance Target**: < 60 seconds (with .cache models)  
**Dependencies**: PostgreSQL, .cache models

**New Tests Required**:
```python
# test_gguf_ingestion.py
async def test_ingest_tinyllama_gguf(db_connection, clean_db, test_gguf_path):
    """Ingest TinyLlama GGUF using geometric atomization."""
    atomizer = GeometricAtomizer()
    
    # Read GGUF tensors
    tensors = load_gguf_tensors(test_gguf_path)
    
    # Atomize all tensors
    for name, tensor in tensors.items():
        trajectory = atomizer.atomize_tensor(tensor, metadata={"name": name})
        
        # Store LINESTRING
        await store_trajectory(db_connection, trajectory)
    
    # Verify compression ratio
    stats = await get_atomization_stats(db_connection)
    assert stats['compression_ratio'] > 500  # Should be ~769x
    assert stats['trajectories_created'] == len(tensors)

# test_spatial_queries.py
async def test_spatial_proximity_search(db_connection, clean_db):
    """Test PostGIS spatial queries on trajectories."""
    # Store multiple trajectories
    t1 = create_test_trajectory([1.0, 2.0, 3.0])
    t2 = create_test_trajectory([1.1, 2.1, 3.1])  # Similar
    t3 = create_test_trajectory([100.0, 200.0, 300.0])  # Distant
    
    await store_trajectory(db_connection, t1)
    await store_trajectory(db_connection, t2)
    await store_trajectory(db_connection, t3)
    
    # Find neighbors of t1 within distance threshold
    neighbors = await find_spatial_neighbors(db_connection, t1, distance=5.0)
    
    assert t2.id in neighbors  # Should find similar trajectory
    assert t3.id not in neighbors  # Should not find distant trajectory
```

### Phase 3: Performance Benchmarks

**Location**: `tests/performance/geometric/`

**Structure**:
```
tests/performance/geometric/
├── test_atomization_speed.py     # 318K ops/sec benchmark
├── test_compression_ratios.py    # 769x compression verification
├── test_spatial_indexing.py      # Hilbert curve performance
├── test_memory_usage.py          # Memory profiling
└── test_scalability.py           # Large model stress tests
```

**Benchmark Targets**:
- Atomization speed: ≥ 318,000 ops/sec
- Compression ratio: ≥ 769x
- Memory usage: < 1GB for TinyLlama ingestion
- Spatial queries: < 100ms for proximity search

**New Benchmarks**:
```python
# test_atomization_speed.py
@pytest.mark.performance
def test_atomization_throughput():
    """Verify 318K ops/sec atomization speed."""
    atomizer = GeometricAtomizer()
    
    # Generate 1M test values
    test_data = np.random.rand(1_000_000).astype(np.float32)
    
    start = time.time()
    for value in test_data:
        _ = atomizer.atomize_value(value)
    elapsed = time.time() - start
    
    ops_per_sec = len(test_data) / elapsed
    assert ops_per_sec >= 318_000, f"Only {ops_per_sec:.0f} ops/sec"

# test_compression_ratios.py
@pytest.mark.performance
async def test_compression_ratio_target(db_connection, test_gguf_path):
    """Verify 769x compression ratio on TinyLlama."""
    atomizer = GeometricAtomizer()
    
    # Ingest full model
    stats = await atomizer.ingest_gguf(test_gguf_path, db_connection)
    
    original_size = stats['raw_tensor_bytes']
    compressed_size = stats['atom_bytes']
    ratio = original_size / compressed_size
    
    assert ratio >= 769, f"Only {ratio:.1f}x compression"
```

### Phase 4: Shared Fixtures & Utilities

**Location**: `tests/conftest.py` (enhanced)

**New Fixtures**:
```python
@pytest.fixture(scope="session")
def geometric_atomizer():
    """Reusable GeometricAtomizer instance."""
    return GeometricAtomizer()

@pytest.fixture(scope="session")
def fractal_atomizer():
    """Reusable FractalAtomizer instance."""
    return FractalAtomizer()

@pytest.fixture(scope="session")
def bpe_crystallizer():
    """Reusable BPECrystallizer instance."""
    return BPECrystallizer()

@pytest.fixture
async def sample_trajectory(db_connection, clean_db):
    """Pre-stored sample trajectory for tests."""
    atomizer = GeometricAtomizer()
    trajectory = atomizer.atomize_tensor(
        np.array([1.0, 2.0, 3.0, 4.0, 5.0]),
        metadata={"name": "test_trajectory"}
    )
    trajectory_id = await store_trajectory(db_connection, trajectory)
    return trajectory_id, trajectory

@pytest.fixture
async def ingested_tinyllama(db_connection, clean_db, test_gguf_path):
    """Full TinyLlama ingestion (expensive, session-scoped)."""
    atomizer = GeometricAtomizer()
    stats = await atomizer.ingest_gguf(test_gguf_path, db_connection)
    return stats
```

### Phase 5: Archive/Remove Legacy Tests

**Archive** (move to `tests/archive/`):
- `tests/integration/test_gguf_atomizer.py` (CompositionBuilder)
- `tests/integration/test_tensor_reconstruction.py` (TensorAtomizer)
- `tests/performance/test_model_ingestion.py` (legacy GGUFAtomizer)
- `tests/performance/test_safetensors_ingestion.py` (legacy)
- `tests/sql/test_atom_composition.py` (tests deprecated table)

**Delete** (no migration path):
- Tests that explicitly test deprecated CompositionBuilder

**Rationale**: Keep for historical reference but mark as deprecated

## Implementation Phases

### Phase 1: Foundation (Days 1-2)
- [ ] Create `tests/unit/geometric/` directory structure
- [ ] Split `test_geometric_atomization.py` into focused unit tests
- [ ] Split `test_fractal_atomization.py` into unit tests
- [ ] Add mocked DB fixtures for pure unit testing
- [ ] Ensure 100% coverage of AtomLocator, TrajectoryBuilder, SpatialReconstructor

**Success Criteria**: All unit tests pass in < 1 second, no DB required

### Phase 2: Integration (Days 3-4)
- [ ] Create `tests/integration/geometric/` directory
- [ ] Write `test_gguf_ingestion.py` using TinyLlama
- [ ] Write `test_safetensors_ingestion.py` using MiniLM
- [ ] Write `test_spatial_queries.py` for PostGIS operations
- [ ] Write `test_fractal_reconstruction.py` for bit-perfect verification
- [ ] Write `test_compression_ratios.py` to verify 769x claim

**Success Criteria**: All integration tests pass in < 60 seconds with .cache models

### Phase 3: Performance (Days 5-6)
- [ ] Create `tests/performance/geometric/` directory
- [ ] Write `test_atomization_speed.py` (318K ops/sec benchmark)
- [ ] Write `test_compression_ratios.py` (769x verification)
- [ ] Write `test_spatial_indexing.py` (Hilbert performance)
- [ ] Write `test_memory_usage.py` (memory profiling)
- [ ] Write `test_scalability.py` (stress tests)

**Success Criteria**: All benchmarks meet targets (318K ops/sec, 769x compression)

### Phase 4: Cleanup (Day 7)
- [ ] Archive legacy tests to `tests/archive/`
- [ ] Delete tests for CompositionBuilder
- [ ] Update conftest.py with new fixtures
- [ ] Update `tests/README.md` with new structure
- [ ] Update `docs/TESTING_GUIDE.md`
- [ ] Fix 2 failing tests in test_code_atomizer_integration.py
- [ ] Fix hung test in test_fractal_ingestion_pipeline.py

**Success Criteria**: Zero legacy tests in active suite, all tests pass

### Phase 5: Documentation (Day 8)
- [ ] Update test docstrings with clear purpose statements
- [ ] Add test architecture diagram to README
- [ ] Document fixture usage patterns
- [ ] Create test writing guide for new developers
- [ ] Add CI/CD integration examples
- [ ] Document performance benchmark baselines

**Success Criteria**: Complete test documentation, onboarding guide exists

## File Organization (Target State)

```
tests/
├── conftest.py                 # Enhanced fixtures (geometric atomizers, models)
├── README.md                   # Updated architecture overview
│
├── unit/                       # Pure unit tests (no DB, < 1s)
│   └── geometric/
│       ├── test_atom_locator.py
│       ├── test_trajectory_builder.py
│       ├── test_spatial_reconstructor.py
│       ├── test_fractal_atomizer.py
│       ├── test_bpe_crystallizer.py
│       └── test_edge_cases.py
│
├── integration/                # DB integration (< 60s)
│   ├── geometric/
│   │   ├── test_gguf_ingestion.py
│   │   ├── test_safetensors_ingestion.py
│   │   ├── test_spatial_queries.py
│   │   ├── test_fractal_reconstruction.py
│   │   ├── test_compression_ratios.py
│   │   └── test_end_to_end_workflow.py
│   │
│   ├── test_api.py             # FastAPI endpoints
│   ├── test_database.py        # DB connection tests
│   └── test_code_parser.py     # Code atomizer
│
├── performance/                # Benchmarks (variable duration)
│   └── geometric/
│       ├── test_atomization_speed.py    # 318K ops/sec
│       ├── test_compression_ratios.py   # 769x compression
│       ├── test_spatial_indexing.py     # Hilbert performance
│       ├── test_memory_usage.py         # Memory profiling
│       └── test_scalability.py          # Stress tests
│
├── functional/                 # End-to-end (< 30s)
│   ├── test_compression.py
│   ├── test_hilbert.py
│   └── test_positioning.py
│
├── smoke/                      # Quick validation (< 5s)
│   ├── test_imports.py
│   └── test_connection.py
│
├── sql/                        # PostgreSQL functions
│   ├── test_hilbert_sql.py
│   ├── test_spatial_functions.py
│   └── test_atomize_value.py
│
└── archive/                    # Legacy brownfield tests
    ├── test_gguf_atomizer.py   # CompositionBuilder version
    ├── test_tensor_reconstruction.py  # TensorAtomizer version
    └── test_atom_composition.py  # Deprecated table tests
```

## Test Execution Strategy

### Development (Fast Feedback)
```bash
# Quick unit tests only (< 1s)
pytest tests/unit/geometric/ -v

# Unit + smoke (< 10s)
pytest tests/unit/ tests/smoke/ -v
```

### Pre-Commit (Comprehensive)
```bash
# All except performance (< 90s)
pytest tests/ --ignore=tests/performance --ignore=tests/archive -v
```

### CI/CD Pipeline
```bash
# Stage 1: Smoke (< 5s) - Fails fast
pytest tests/smoke/ -v --tb=short

# Stage 2: Unit (< 5s) - High coverage
pytest tests/unit/ -v --cov=api/services/geometric_atomization --cov-report=html

# Stage 3: Integration (< 60s) - Real DB
pytest tests/integration/geometric/ tests/integration/test_database.py -v

# Stage 4: Functional (< 30s) - End-to-end
pytest tests/functional/ -v

# Stage 5: SQL (< 20s) - PostgreSQL functions
pytest tests/sql/ -v

# Stage 6: Performance (manual trigger) - Benchmarks
pytest tests/performance/geometric/ -v --benchmark-only
```

### Performance Benchmarking (Manual)
```bash
# Run all benchmarks with detailed output
pytest tests/performance/geometric/ -v -s --benchmark-only

# Specific benchmark
pytest tests/performance/geometric/test_atomization_speed.py::test_atomization_throughput -v -s

# Compare against baseline
pytest tests/performance/geometric/ --benchmark-compare=baseline.json
```

## Success Metrics

**Test Coverage**:
- Unit tests: 100% of geometric atomization code
- Integration tests: 100% of model ingestion pipelines
- Overall: ≥ 85% code coverage

**Performance**:
- Unit tests: < 1 second total
- Integration tests: < 60 seconds with .cache models
- Smoke tests: < 5 seconds

**Quality**:
- Zero failing tests
- Zero flaky tests (no hangs, no intermittent failures)
- Zero brownfield tests in active suite

**Documentation**:
- Every test has clear docstring
- README explains test architecture
- CI/CD pipeline documented
- Performance baselines recorded

## Migration Checklist

### Unit Tests
- [ ] tests/geometric/test_geometric_atomization.py → split into 5 focused tests
- [ ] tests/geometric/test_fractal_atomization.py → test_fractal_atomizer.py
- [ ] tests/geometric/test_fractal_edge_cases.py → test_edge_cases.py + test_bpe_crystallizer.py
- [ ] tests/geometric/test_bpe_integration.py → test_bpe_crystallizer.py

### Integration Tests
- [ ] NEW: test_gguf_ingestion.py (TinyLlama with geometric atomization)
- [ ] NEW: test_safetensors_ingestion.py (MiniLM with geometric atomization)
- [ ] NEW: test_spatial_queries.py (PostGIS trajectory queries)
- [ ] NEW: test_fractal_reconstruction.py (bit-perfect reconstruction)
- [ ] NEW: test_compression_ratios.py (verify 769x compression)
- [ ] FIX: test_code_atomizer_integration.py (2 failing TypeError tests)
- [ ] FIX: test_fractal_ingestion_pipeline.py (hung test, convert to geometric)

### Performance Tests
- [ ] NEW: test_atomization_speed.py (318K ops/sec benchmark)
- [ ] NEW: test_compression_ratios.py (769x compression verification)
- [ ] NEW: test_spatial_indexing.py (Hilbert curve performance)
- [ ] NEW: test_memory_usage.py (memory profiling during ingestion)
- [ ] NEW: test_scalability.py (large model stress tests)

### Archive/Delete
- [ ] ARCHIVE: tests/integration/test_gguf_atomizer.py → tests/archive/
- [ ] ARCHIVE: tests/integration/test_tensor_reconstruction.py → tests/archive/
- [ ] ARCHIVE: tests/performance/test_model_ingestion.py → tests/archive/
- [ ] ARCHIVE: tests/performance/test_safetensors_ingestion.py → tests/archive/
- [ ] ARCHIVE: tests/sql/test_atom_composition.py → tests/archive/

### Documentation
- [ ] Update tests/README.md with new architecture
- [ ] Update docs/TESTING_GUIDE.md with running strategies
- [ ] Create tests/ARCHITECTURE.md with design rationale
- [ ] Add docstrings to all new tests
- [ ] Document fixture usage patterns

## Notes

**Key Principles**:
1. **Greenfield First**: All new tests use geometric architecture only
2. **Fast Feedback**: Unit tests run in < 1 second
3. **Real Assets**: Integration tests use .cache models (no mocks)
4. **No Flakes**: Zero hanging tests, zero intermittent failures
5. **Clear Purpose**: Every test has explicit docstring explaining what and why

**Migration Priority**:
1. Fix existing failures (2 in code_atomizer, 1 hung test)
2. Create solid unit test foundation
3. Build comprehensive integration tests
4. Add performance benchmarks
5. Archive legacy tests

**Timeline**: 8 days for complete refactoring  
**Resources**: .cache models (TinyLlama ~637MB, MiniLM ~87MB), PostgreSQL 16 + PostGIS

**Outcome**: Production-ready test suite with 100% greenfield coverage, zero brownfield dependencies
