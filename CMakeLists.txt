cmake_minimum_required(VERSION 3.25)

project(Hartonomous LANGUAGES C CXX)

# Enforce C++ standard and deterministic behavior
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Enable reproducible builds
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Load external dependency configs
include(cmake/mkl-config.cmake)
include(cmake/eigen-config.cmake)
include(cmake/spectra-config.cmake)
include(cmake/hnsw-config.cmake)
include(cmake/blake3-config.cmake)

# Add subprojects
add_subdirectory(Engine)
add_subdirectory(PostgresExtension)
