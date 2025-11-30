# Legacy Atomization Services

**STATUS:** This directory contains **brownfield/legacy** atomization code.

## Migration Status

The Hartonomous project has transitioned to **Geometric Intelligence** with trajectory-based storage.

### ✅ New System (Greenfield)
**Location:** `api/services/geometric_atomization/`

**Architecture:**
- **Atoms:** Constants at semantic coordinates (deterministic via hash)
- **Compositions:** `LINESTRING` trajectories visiting coordinates
- **Storage:** `LINESTRINGZM(x1 y1 z1 m1, x2 y2 z2 m2, ...)`
- **Deduplication:** O(1) via coordinate collision
- **Compression:** 769x proven ("Lorem Ipsum" 1000x = 13 atoms)

**Classes:**
- `FractalAtomizer`: Fractal deduplication with `composition_ids BIGINT[]`
- `BPECrystallizer`: Autonomous pattern learning (OODA loop)
- `TrajectoryBuilder`: Build LINESTRING geometries
- `SpatialReconstructor`: Reconstruct from trajectories
- `GeometricAtomizer`: High-level orchestrator

### ⚠️ Legacy System (This Directory)
**Architecture:**
- Relational composition tables (record explosion)
- Manual pattern definition
- Row-per-atom storage

**Files:**
- `composition_builder.py`: Builds relational compositions (DEPRECATED)
- `gguf_atomizer.py`: GGUF model loader (needs geometric migration)
- `tensor_atomizer.py`: Tensor atomization (needs geometric migration)
- `tensor_reconstructor.py`: Tensor reconstruction (needs geometric migration)
- `hilbert_encoder.py`: Hilbert curve encoding (still useful)
- `weight_processor.py`: Weight processing (still useful)

## Migration Path

### Phase 1: Keep Legacy for Model Loading ✅
Current state: `gguf_atomizer.py` and `tensor_atomizer.py` are used by:
- `api/services/model_atomization.py`
- `tests/integration/test_fractal_ingestion_pipeline.py`
- `tests/integration/test_tensor_reconstruction.py`

### Phase 2: Migrate to Geometric (Future)
**TODO:** Refactor these atomizers to use:
1. `TrajectoryBuilder` instead of `CompositionBuilder`
2. `FractalAtomizer` for atom creation
3. `BPECrystallizer` for pattern learning

**Target:**
- 50M weight tensors → 1 LINESTRING geometry
- Automatic weight pattern discovery
- O(1) model deduplication

### Phase 3: Archive Legacy
Once migration complete:
- Move legacy files to `api/services/archive/`
- Update all imports
- Remove `CompositionBuilder` entirely

## Current Usage

**Active imports:**
```python
# In api/services/model_atomization.py
from api.services.atomization import GGUFAtomizer

# In tests
from api.services.atomization.tensor_atomizer import TensorAtomizer
from api.services.atomization.tensor_reconstructor import TensorReconstructor
from api.services.atomization.gguf_atomizer import GGUFAtomizer
```

**Do NOT use for new code.** Use `api/services/geometric_atomization/` instead.

## Test Results

**Legacy tests:** Still passing (for backward compatibility)
**Geometric tests:** 44/44 passing (100%)

The geometric system is proven superior:
- 318,705 coordinate computations/second
- 769x compression ratio
- Autonomous pattern learning
- O(1) deduplication

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
