# Hartonomous Build Guide

## Prerequisites

### Required Software

#### 1. CMake (3.20+)
```bash
# Windows
winget install Kitware.CMake

# Linux
sudo apt install cmake

# macOS
brew install cmake

# Verify
cmake --version  # Should be 3.20 or higher
```

#### 2. C++20 Compiler

**Windows (MSVC 2022):**
```bash
# Install Visual Studio 2022 Community
# Or Visual Studio Build Tools 2022
# https://visualstudio.microsoft.com/downloads/

# Verify
cl.exe  # Should be version 19.30+
```

**Linux (GCC 11+):**
```bash
sudo apt install g++-11
# Or
sudo apt install g++-12

# Verify
g++ --version  # Should be 11.0 or higher
```

**macOS (Clang 14+):**
```bash
xcode-select --install

# Verify
clang++ --version  # Should be 14.0 or higher
```

#### 3. Intel OneAPI Base Toolkit (MKL)

**Windows:**
```bash
# Download from: https://www.intel.com/content/www/us/en/developer/tools/oneapi/base-toolkit-download.html
# Or use winget
winget install Intel.OneAPI.BaseKit

# Add to PATH
setx PATH "%PATH%;C:\Program Files (x86)\Intel\oneAPI\mkl\latest\bin"
```

**Linux:**
```bash
# Download from Intel website or use package manager
wget https://registrationcenter-download.intel.com/akdlm/IRC_NAS/.../l_BaseKit_p_2024.0.0.49564_offline.sh
sh ./l_BaseKit_p_2024.0.0.49564_offline.sh

# Source environment
source /opt/intel/oneapi/setvars.sh
```

**macOS:**
```bash
# Download from Intel website
# https://www.intel.com/content/www/us/en/developer/tools/oneapi/base-toolkit-download.html

# Source environment
source /opt/intel/oneapi/setvars.sh
```

#### 4. PostgreSQL 15+ with PostGIS 3.3+

**Windows:**
```bash
# Download from: https://www.postgresql.org/download/windows/
# Or use chocolatey
choco install postgresql15

# Install PostGIS
# Download from: https://postgis.net/windows_downloads/
```

**Linux:**
```bash
sudo apt install postgresql-15 postgresql-15-postgis-3
```

**macOS:**
```bash
brew install postgresql@15 postgis
```

### Optional (for development)

- **Ninja build system** (faster than make)
  ```bash
  winget install Ninja-build.Ninja  # Windows
  sudo apt install ninja-build      # Linux
  brew install ninja                # macOS
  ```

- **ccache** (speeds up rebuilds)
  ```bash
  winget install ccache.ccache      # Windows
  sudo apt install ccache           # Linux
  brew install ccache               # macOS
  ```

---

## Building from Source

### 1. Clone Repository with Submodules

```bash
git clone --recursive https://github.com/yourorg/Hartonomous.git
cd Hartonomous

# If you already cloned without --recursive:
git submodule update --init --recursive
```

### 2. Verify Submodules

```bash
ls Engine/external/

# Should show:
#   blake3/
#   eigen/
#   hnswlib/
#   spectra/
```

### 3. Configure Build

#### Option A: Use Presets (Recommended)

**Release build with native CPU optimizations:**
```bash
cmake --preset release-native
```

**Release build with portable binary (no -march=native):**
```bash
cmake --preset release-portable
```

**Debug build:**
```bash
cmake --preset debug
```

**Available presets:**
- `release-native`: Optimized for your CPU (not portable)
- `release-portable`: Optimized but portable (SSE4.1 minimum)
- `release-avx512`: Requires AVX-512 support
- `release-avx2`: Requires AVX2 support
- `debug`: Debug symbols, no optimizations

#### Option B: Manual Configuration

**Windows (MSVC):**
```cmd
cmake -B build ^
  -G "Visual Studio 17 2022" ^
  -A x64 ^
  -DCMAKE_BUILD_TYPE=Release ^
  -DHARTONOMOUS_ENABLE_NATIVE_ARCH=ON ^
  -DHARTONOMOUS_MKL_THREADING=INTEL ^
  -DHARTONOMOUS_MKL_INTERFACE=LP64 ^
  -DHARTONOMOUS_HNSW_SIMD=AUTO
```

**Linux (GCC/Clang):**
```bash
cmake -B build \
  -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_CXX_COMPILER=g++ \
  -DHARTONOMOUS_ENABLE_NATIVE_ARCH=ON \
  -DHARTONOMOUS_MKL_THREADING=INTEL \
  -DHARTONOMOUS_MKL_INTERFACE=LP64 \
  -DHARTONOMOUS_HNSW_SIMD=AUTO
```

**macOS (Clang):**
```bash
cmake -B build \
  -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_CXX_COMPILER=clang++ \
  -DHARTONOMOUS_ENABLE_NATIVE_ARCH=ON \
  -DHARTONOMOUS_MKL_THREADING=INTEL \
  -DHARTONOMOUS_MKL_INTERFACE=LP64 \
  -DHARTONOMOUS_HNSW_SIMD=AUTO
```

### 4. Build

```bash
# Build all targets
cmake --build build/release-native -j

# Or specify number of parallel jobs
cmake --build build/release-native -j 8

# Build specific target
cmake --build build/release-native --target engine -j
cmake --build build/release-native --target hartonomous_extension -j
```

### 5. Run Tests

```bash
cd build/release-native
ctest --output-on-failure

# Or run specific tests
./Engine/tests/test_hopf_fibration
./Engine/tests/test_super_fibonacci
./Engine/tests/test_hilbert_curve
```

### 6. Install

**PostgreSQL Extension:**
```bash
cd build/release-native/PostgresExtension
sudo make install

# Verify
psql -c "CREATE EXTENSION hartonomous;"
psql -c "SELECT hartonomous_version();"
```

**C++ Library:**
```bash
cd build/release-native
sudo cmake --install .

# Or specify prefix
cmake --install . --prefix /usr/local
```

---

## Build Configuration Options

### Performance Options

```cmake
# Enable native CPU architecture optimizations (-march=native / /arch:AVX512)
# Default: ON
# WARNING: Binary will not be portable to other CPUs
option(HARTONOMOUS_ENABLE_NATIVE_ARCH "Enable native CPU optimizations" ON)

# MKL threading layer: SEQUENTIAL, INTEL, TBB, GNU
# Default: SEQUENTIAL
# INTEL = Intel OpenMP (best performance with Intel compiler)
# TBB = Intel Threading Building Blocks
# GNU = GNU OpenMP (use with GCC)
set(HARTONOMOUS_MKL_THREADING "SEQUENTIAL" CACHE STRING "MKL threading layer")

# MKL interface: LP64 (32-bit integers) or ILP64 (64-bit integers)
# Default: LP64
# Use ILP64 for matrices with >2^31 elements
set(HARTONOMOUS_MKL_INTERFACE "LP64" CACHE STRING "MKL interface (LP64 or ILP64)")

# HNSWLib SIMD mode: AUTO, SSE, AVX, AVX512, NONE
# Default: AUTO (detect at compile time)
set(HARTONOMOUS_HNSW_SIMD "AUTO" CACHE STRING "HNSWLib SIMD optimization")
```

### Example Configurations

**Maximum Performance (Intel CPU with AVX-512):**
```bash
cmake --preset release-native \
  -DHARTONOMOUS_MKL_THREADING=INTEL \
  -DHARTONOMOUS_MKL_INTERFACE=LP64 \
  -DHARTONOMOUS_HNSW_SIMD=AVX512
```

**Portable Binary (minimum SSE4.1):**
```bash
cmake --preset release-portable \
  -DHARTONOMOUS_ENABLE_NATIVE_ARCH=OFF \
  -DHARTONOMOUS_HNSW_SIMD=SSE
```

**Large Matrices (>2^31 elements):**
```bash
cmake --preset release-native \
  -DHARTONOMOUS_MKL_INTERFACE=ILP64
```

**Multi-threaded MKL (with Intel OpenMP):**
```bash
cmake --preset release-native \
  -DHARTONOMOUS_MKL_THREADING=INTEL
```

---

## Troubleshooting

### CMake Can't Find Intel MKL

**Problem:**
```
CMake Error: Could not find Intel MKL
```

**Solution:**
```bash
# Windows
setx MKLROOT "C:\Program Files (x86)\Intel\oneAPI\mkl\latest"

# Linux/macOS
export MKLROOT=/opt/intel/oneapi/mkl/latest
source /opt/intel/oneapi/setvars.sh

# Then re-run cmake
```

### Compiler Doesn't Support C++20

**Problem:**
```
CMake Error: The compiler does not support C++20
```

**Solution:**
```bash
# Upgrade compiler
# Windows: Install Visual Studio 2022
# Linux: sudo apt install g++-12
# macOS: xcode-select --install

# Or specify newer compiler
cmake -B build -DCMAKE_CXX_COMPILER=g++-12
```

### BLAKE3 SIMD Variants Not Compiling

**Problem:**
```
error: '__m512i' was not declared in this scope
```

**Solution:**
Check CPU supports AVX-512:
```bash
# Linux/macOS
grep avx512 /proc/cpuinfo

# Windows
wmic cpu get caption

# If not supported, disable AVX-512 variants in cmake/blake3-config.cmake
```

### PostgreSQL Extension Installation Fails

**Problem:**
```
error: pg_config not found
```

**Solution:**
```bash
# Add pg_config to PATH
# Windows
setx PATH "%PATH%;C:\Program Files\PostgreSQL\15\bin"

# Linux
sudo apt install postgresql-server-dev-15

# macOS
export PATH="/opt/homebrew/opt/postgresql@15/bin:$PATH"
```

### Link-Time Optimization (LTO) Fails

**Problem:**
```
error: lto1: internal compiler error
```

**Solution:**
```bash
# Disable LTO
cmake -B build -DHARTONOMOUS_ENABLE_LTO=OFF
```

### Out of Memory During Build

**Problem:**
```
c++: fatal error: Killed signal terminated program cc1plus
```

**Solution:**
```bash
# Reduce parallel jobs
cmake --build build -j 2

# Or add swap space (Linux)
sudo dd if=/dev/zero of=/swapfile bs=1G count=8
sudo mkswap /swapfile
sudo swapon /swapfile
```

---

## Verification

### 1. Test BLAKE3 SIMD

```bash
cd build/release-native/Engine
./blake3_benchmark

# Expected output:
# BLAKE3 (AVX-512): 3.2 GB/s
# BLAKE3 (AVX2):    2.1 GB/s
# BLAKE3 (SSE4.1):  1.5 GB/s
# BLAKE3 (SSE2):    1.2 GB/s
# BLAKE3 (portable): 0.8 GB/s
```

### 2. Test MKL Integration

```bash
cd build/release-native/Engine
./mkl_benchmark

# Expected output:
# MKL version: 2024.0
# MKL threading: INTEL
# MKL interface: LP64
# DGEMM performance: 250 GFLOPS (varies by CPU)
```

### 3. Test 4D Geometry

```bash
cd build/release-native/Engine
./test_hopf_fibration
./test_super_fibonacci
./test_hilbert_curve

# Expected output:
# [  PASSED  ] All tests
```

### 4. Test PostgreSQL Extension

```sql
-- Create test database
CREATE DATABASE hartonomous_test;
\c hartonomous_test

-- Install extensions
CREATE EXTENSION postgis;
CREATE EXTENSION hartonomous;

-- Verify versions
SELECT postgis_version();
SELECT hartonomous_version();

-- Test 4D distance function
SELECT st_distance_s3(0.5, 0.5, 0.5, 0.5, 0.6, 0.6, 0.6, 0.6);
-- Expected: ~0.2 (geodesic distance on S³)

-- Test Hilbert encoding
SELECT hilbert_encode_4d(0.5, 0.5, 0.5, 0.5, 16);
-- Expected: some integer value

-- Clean up
\c postgres
DROP DATABASE hartonomous_test;
```

---

## Performance Benchmarks

### Expected Performance (Intel i7-12700K, 32 GB RAM, NVMe SSD)

**BLAKE3 Hashing:**
- AVX-512: ~3.5 GB/s (single-threaded)
- AVX2: ~2.5 GB/s
- Throughput: ~8 million hashes/sec for 1 KB blocks

**Hilbert Curve Encoding:**
- 4D, 16 bits: ~50 million encodings/sec
- 4D, 32 bits: ~20 million encodings/sec

**MKL Matrix Operations:**
- DGEMM (1024x1024): ~300 GFLOPS
- Eigenvalue decomposition (1024x1024): ~2 seconds

**PostgreSQL Queries:**
- Spatial lookup (GiST index, 1M compositions): <5ms
- Relationship traversal (depth 3, 1M relations): <20ms
- A* pathfinding (average graph, 10K nodes): <100ms

---

## Next Steps

1. **Run Tests:** Verify all components work correctly
2. **Ingest Sample Data:** Try ingesting "Call me Ishmael"
3. **Profile Performance:** Identify bottlenecks
4. **Read [IMPLEMENTATION_ROADMAP.md](IMPLEMENTATION_ROADMAP.md)** for next implementation phases

---

## Build Presets Reference

### CMakePresets.json

All available presets:

| Preset | Description | Optimization | Portable? |
|--------|-------------|--------------|-----------|
| `release-native` | Maximum performance for your CPU | -O3 -march=native | ❌ No |
| `release-portable` | Optimized but portable | -O3 -msse4.1 | ✅ Yes |
| `release-avx512` | Requires AVX-512 | -O3 -mavx512f | ❌ No |
| `release-avx2` | Requires AVX2 | -O3 -mavx2 | ⚠️ Mostly |
| `debug` | Development/debugging | -O0 -g | ✅ Yes |
| `relwithdebinfo` | Optimized + debug symbols | -O2 -g | ✅ Yes |

Use with:
```bash
cmake --preset <preset-name>
cmake --build build/<preset-name> -j
```

---

**For questions or issues, see [README.md](README.md) or open an issue on GitHub.**
