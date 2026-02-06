---
name: cross-platform-build-mgmt
description: Build Hartonomous with CMake. Manage MKL linkage, OBJECT libraries, PostgreSQL extension builds, and optimization flags.
---

# Build Management

## CMake Structure
```
CMakeLists.txt                    -- Root: dependencies, presets
CMakePresets.json                 -- linux-release-max-perf (primary)
cmake/*.cmake                     -- Find modules (MKL, PostGIS, etc.)
Engine/CMakeLists.txt             -- 3 shared libs + tools + tests
Engine/cmake/SourceLists.cmake    -- Explicit source file lists
PostgresExtension/CMakeLists.txt  -- s3.so + hartonomous.so
```

## How to Build

```bash
# Full pipeline (build → install → db → seed → ingest → test)
./full-send.sh

# Build only (75 targets)
cmake --preset linux-release-max-perf
cmake --build build/linux-release-max-perf -j$(nproc)

# Quick dev rebuild (after one-time symlink setup)
./rebuild.sh --clean --test

# Run unit tests (20 tests)
cd build/linux-release-max-perf
LD_LIBRARY_PATH="$PWD/Engine:$LD_LIBRARY_PATH" ctest --output-on-failure -L unit
```

## OBJECT Library Pattern
Sources compile once, link into 3 shared libs:
```cmake
add_library(engine_core_objs OBJECT ${CORE_SOURCES})  # Math/geometry
add_library(engine_io_objs OBJECT ${IO_SOURCES})       # DB/ingestion

add_library(engine_core SHARED $<TARGET_OBJECTS:engine_core_objs>)
add_library(engine_io SHARED $<TARGET_OBJECTS:engine_io_objs>)
add_library(engine SHARED $<TARGET_OBJECTS:engine_core_objs> $<TARGET_OBJECTS:engine_io_objs>)
```

**Rule: Link against shared libs (`engine_core`, `engine_io`, `engine`), NEVER against `*_objs` targets.**

## Adding a New Tool
```cmake
# In Engine/tools/CMakeLists.txt — just link engine_io
add_executable(my_tool src/my_tool.cpp)
target_link_libraries(my_tool PRIVATE engine_io)
```
`engine_io` transitively provides all includes, MKL, PostgreSQL, BLAKE3, etc.

## Dependencies
| Dependency | Type | Purpose |
|-----------|------|---------|
| Intel MKL | System (`source /opt/intel/oneapi/setvars.sh`) | BLAS/LAPACK for Eigen, Spectra |
| PostgreSQL 18 | System (`pg_config`) | Extension hosting, libpq |
| PostGIS | System or submodule | 4D geometry types (POINTZM) |
| BLAKE3 | Submodule (`Engine/external/blake3/`) | Content-addressing, per-file SIMD |
| Eigen | Header-only (`Engine/external/eigen/`) | Linear algebra |
| HNSWLib | Header-only (`Engine/external/hnswlib/`) | k-NN graph construction |
| tree-sitter | Submodule (`Engine/external/tree-sitter/`) | AST parsing for code ingestion |
| nlohmann/json | Header-only (`Engine/external/json/`) | Config/metadata |
| hilbert_hpp | Header-only (`Engine/external/hilbert/`) | 128-bit Hilbert encoding |
| Spectra | Header-only (`Engine/external/spectra/`) | Large eigenvalue problems |

## Troubleshooting
- **MKL not found**: `source /opt/intel/oneapi/setvars.sh` before cmake
- **PostgreSQL headers missing**: `sudo apt install postgresql-server-dev-18`
- **Shared lib not found at runtime**: `LD_LIBRARY_PATH=build/linux-release-max-perf/Engine:$LD_LIBRARY_PATH`
- **Stale build artifacts**: `rm -rf build/linux-release-max-perf && cmake --preset linux-release-max-perf`