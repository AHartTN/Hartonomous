---
name: cross-platform-build-mgmt
description: Manage CMake presets and cross-platform build environments (Linux/Windows). Use when troubleshooting dependency linkage (MKL, Eigen) or PowerShell/Bash build scripts.
---

# Cross-Platform Build Management

This skill ensures that the Hartonomous ecosystem can be reliably built and tested on both Linux (Server) and Windows (Client).

## The Build Architecture

### 1. Parity Presets
Implementation: `CMakePresets.json`
- **SIMD Parity**: Enforce AVX-512/AVX2 parity across compilers.
  - Linux (GCC/Clang): `-march=native`.
  - Windows (MSVC): `/arch:AVX512` or `/arch:AVX2`.
- **MKL Linkage**:
  - Linux: Dynamic linkage against `libmkl_rt.so`.
  - Windows: Static/Dynamic linkage against `mkl_rt.lib`.

### 2. Dependency Management
- **MKL/Eigen**: Managed via `cmake/*-config.cmake`. Requires `MKLROOT` to be set correctly.
- **HNSWLib**: Header-only, requires specific SIMD level configuration (`HARTONOMOUS_HNSW_SIMD`).
- **Postgres**: Requires `find_package(PostgreSQL)` and headers for extension development.

## Build Scripts
- **Linux**: `full-send.sh` handles the full sequence from seeding Atoms to starting the API.
- **Windows**: `scripts/windows/powershell/` provides a mirror of the Linux functionality for Windows-based ingestion clients.

## Maintenance Workflow
1.  **Preset Validation**: Confirm that a new dependency works across both Ninja/GCC and Ninja/MSVC.
2.  **Environment Sync**: Ensure `LD_LIBRARY_PATH` (Linux) and `PATH` (Windows) are updated to include the build artifacts.
3.  **CI Audit**: Run tests via `ctest` on Linux and `03_UnitTest.ps1` on Windows.
4.  **Symbol Export**: Ensure all C++ exports use `HARTONOMOUS_API` for `__declspec(dllexport)` parity.