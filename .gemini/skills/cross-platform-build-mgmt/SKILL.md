---
name: cross-platform-build-mgmt
description: Manage CMake build system across Linux/Windows. Handle Intel MKL linkage, OBJECT libraries, PostgreSQL extension builds, and optimization flags.
---

# Cross-Platform Build Management

This skill ensures Hartonomous builds reliably on Linux (server/development) and Windows (future client support).

## Build Architecture

### CMake Structure
```
CMakeLists.txt                    -- Root configuration
CMakePresets.json                 -- Platform presets
cmake/*.cmake                     -- Find modules (MKL, PostGIS, etc.)
Engine/CMakeLists.txt             -- Core engine targets
PostgresExtension/CMakeLists.txt  -- s3.so + hartonomous.so
UCDIngestor/CMakeLists.txt        -- Unicode metadata tool
```

### OBJECT Library Pattern
**Key optimization: Build source once, link multiple times (~40-50% faster)**

```cmake
# Build source to object files
add_library(engine_core_obj OBJECT
    src/geometry/s3_distance.cpp
    src/hashing/blake3_wrapper.cpp
    # ...
)

# Link objects into shared library
add_library(engine_core SHARED)
target_link_libraries(engine_core PRIVATE engine_core_obj)

# Reuse same objects for another target
add_library(engine_test STATIC)
target_link_libraries(engine_test PRIVATE engine_core_obj)
```

## Platform-Specific Configuration

### Linux (Primary Platform)
- **Compiler**: GCC 11+ or Clang 14+
- **Preset**: `linux-release-max-perf` (default)
- **Flags**: `-march=native -O3 -DNDEBUG`
- **MKL Linking**: Dynamic via `libmkl_rt.so`
- **PostgreSQL**: Uses `pg_config` to find headers/libs

### Windows (Future Support)
- **Compiler**: MSVC 2022+
- **Preset**: `windows-release` (TBD)
- **Flags**: `/O2 /arch:AVX2 /DNDEBUG`
- **MKL Linking**: Static or dynamic via `mkl_rt.lib`
- **PostgreSQL**: Manual path configuration required

## Critical Dependencies

### 1. Intel MKL (Required)
**Purpose**: BLAS/LAPACK for geometric operations (eigenmaps, QR decomposition)

```bash
# Install Intel oneAPI Base Toolkit
# Source environment
source /opt/intel/oneapi/setvars.sh

# CMake will find via MKLROOT environment variable
```

**Usage in code**:
- Laplacian eigendecomposition (Spectra + MKL backend)
- Gram-Schmidt orthonormalization (LAPACK QR)
- Dense matrix operations via Eigen with MKL backend

### 2. PostgreSQL 18 + PostGIS (Custom Build)
**Custom PostGIS**: Built with MKL support for enterprise-grade spatial indexing

```bash
# Standard PostGIS (no MKL)
sudo apt install postgresql-18-postgis-3

# Custom build (with MKL) - see docs/BUILD.md
# Required for optimal 4D spatial index performance
```

### 3. Header-Only Libraries (Bundled)
- **Eigen**: Template-based linear algebra
- **Spectra**: Large-scale eigenvalue problems
- **HNSWLib**: Approximate k-NN
- **json**: nlohmann::json for config/metadata
- **hilbert_hpp**: 128-bit Hilbert curve encoding

### 4. Git Submodules (External)
```bash
git submodule update --init --recursive
# Populates Engine/external/
```

## Build Workflow

### Fast Iteration (Development)
```bash
# After changes to Engine/src/
./rebuild.sh
# Rebuilds, copies to install/, runs ldconfig

# Run tests
./scripts/test/run-unit-tests.sh
```

### Full Clean Build
```bash
# Complete pipeline
./full-send.sh
# Or step-by-step:
./scripts/build/build-all.sh
./scripts/build/install-local.sh
sudo ./scripts/build/install-dev-symlinks.sh  # ONE TIME
sudo ldconfig
```

### Build Targets
```bash
cd build/linux-release-max-perf

# Libraries
ninja engine_core      # Pure math/geometry (no DB)
ninja engine_io        # Database integration
ninja engine           # Unified (C# interop)

# PostgreSQL Extensions
ninja s3               # s3.so (S³ operators)
ninja hartonomous_ext  # hartonomous.so (full substrate)

# Tools
ninja seed_unicode     # Populate Atoms
ninja ingest_model     # Extract sparse relations from models
ninja ingest_text      # Build Composition/Relation layers
ninja walk_test        # Graph navigation testing

# Tests
ninja test_hashing
ninja test_geometry
# ... (22 unit tests total)
```

## Optimization Flags

### Release Build (Production)
```cmake
-DCMAKE_BUILD_TYPE=Release
-DHARTONOMOUS_ENABLE_NATIVE_ARCH=ON  # -march=native
-DHARTONOMOUS_MKL_THREADING=GNU      # GNU OpenMP
```

### Debug Build
```cmake
-DCMAKE_BUILD_TYPE=Debug
-DHARTONOMOUS_ENABLE_NATIVE_ARCH=OFF # Portable
-DHARTONOMOUS_ENABLE_ASAN=ON         # Address Sanitizer
```

## Troubleshooting

### MKL Not Found
```bash
# Set environment
export MKLROOT=/opt/intel/oneapi/mkl/latest
source /opt/intel/oneapi/setvars.sh

# Or specify manually
cmake -DMKL_ROOT=/opt/intel/oneapi/mkl/latest ...
```

### PostgreSQL Headers Missing
```bash
sudo apt install postgresql-server-dev-18
pg_config --includedir-server  # Verify path
```

### Library Not Found at Runtime
```bash
# Check paths
ldconfig -p | grep engine
ldconfig -p | grep mkl

# Update cache
sudo ldconfig

# Or set LD_LIBRARY_PATH
export LD_LIBRARY_PATH=/path/to/install/lib:$LD_LIBRARY_PATH
```