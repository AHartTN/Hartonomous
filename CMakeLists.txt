cmake_minimum_required(VERSION 3.22)

project(Hartonomous LANGUAGES C CXX)

enable_testing()

# ==============================================================================
#  PERFORMANCE CONFIGURATION OPTIONS
# ==============================================================================
#
#  These options control the performance characteristics of the build.
#  Defaults are set for maximum performance on the build machine.
#
# ==============================================================================

option(HARTONOMOUS_ENABLE_NATIVE_ARCH
    "Enable native CPU architecture optimizations (-march=native / /arch:AVX512)" ON)

set(HARTONOMOUS_MKL_THREADING "GNU" CACHE STRING
    "MKL threading layer: SEQUENTIAL, INTEL, TBB, GNU")
    
set_property(CACHE HARTONOMOUS_MKL_THREADING PROPERTY STRINGS
    SEQUENTIAL INTEL TBB GNU)

set(HARTONOMOUS_MKL_INTERFACE "LP64" CACHE STRING
    "MKL interface layer: LP64 (32-bit int), ILP64 (64-bit int)")
set_property(CACHE HARTONOMOUS_MKL_INTERFACE PROPERTY STRINGS
    LP64 ILP64)

set(HARTONOMOUS_HNSW_SIMD "AUTO" CACHE STRING
    "HNSWLib SIMD level: AUTO, AVX512, AVX2, SSE")
set_property(CACHE HARTONOMOUS_HNSW_SIMD PROPERTY STRINGS
    AUTO AVX512 AVX2 SSE)

option(HARTONOMOUS_ENABLE_FAST_MATH
    "Enable fast math optimizations (may reduce IEEE compliance)" OFF)

# ==============================================================================
#  GLOBAL SETTINGS
# ==============================================================================

list(APPEND CMAKE_MODULE_PATH "${CMAKE_SOURCE_DIR}/cmake")

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# FORCE Dynamic Runtime (DLL) for MSVC to match Postgres and MKL
if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
endif()

# ==================== PRINT CONFIGURATION ====================
message(STATUS "")
message(STATUS "========== Hartonomous Build Configuration ==========")
message(STATUS "  C++ Standard:        C++${CMAKE_CXX_STANDARD}")
message(STATUS "  Build Type:          ${CMAKE_BUILD_TYPE}")
message(STATUS "  Native Arch:         ${HARTONOMOUS_ENABLE_NATIVE_ARCH}")
message(STATUS "  Fast Math:           ${HARTONOMOUS_ENABLE_FAST_MATH}")
message(STATUS "  MKL Threading:       ${HARTONOMOUS_MKL_THREADING}")
message(STATUS "  MKL Interface:       ${HARTONOMOUS_MKL_INTERFACE}")
message(STATUS "  HNSW SIMD:           ${HARTONOMOUS_HNSW_SIMD}")
message(STATUS "  IPO/LTO:             ${CMAKE_INTERPROCEDURAL_OPTIMIZATION}")
message(STATUS "======================================================")
message(STATUS "")

# ==============================================================================
#  DEPENDENCIES
# ==============================================================================

include(cmake/mkl-config.cmake)
include(cmake/eigen-config.cmake)
include(cmake/spectra-config.cmake)
include(cmake/hnsw-config.cmake)
include(cmake/blake3-config.cmake)
include(cmake/json-config.cmake)
#include(cmake/hilbert-config.cmake)
include(cmake/postgis-config.cmake)
include(cmake/postgres-config.cmake)
include(cmake/treesitter-config.cmake)

# PostgreSQL (required for extension)
# find_package(PostgreSQL REQUIRED)  <-- REMOVED: Conflicing with manual config
# include_directories(${PostgreSQL_INCLUDE_DIRS}) <-- REMOVED: Conflicing with manual config

# ==============================================================================
#  SUBPROJECTS
# ==============================================================================

add_subdirectory(Engine)
add_subdirectory(PostgresExtension)

# UCDIngestor removed — Engine's built-in UCD pipeline (Engine/src/unicode/ingestor/)
# handles all UCD/UCA parsing, semantic sequencing, and S³ projection.
# UCD data files live in Engine/data/ucd/.
