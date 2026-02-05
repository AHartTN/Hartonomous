# ==============================================================================
# Centralized Dependency Management
# ==============================================================================
# Find and configure all engine dependencies in one place
# ==============================================================================

# OpenMP (required for parallelization)
find_package(OpenMP REQUIRED)

# Note: Other dependencies (BLAKE3, Eigen, HNSW, etc.) are found/configured
# by the root CMakeLists.txt via cmake/ modules. This ensures consistent
# configuration across the entire project.

# Dependencies are made available as imported targets:
# - BLAKE3::BLAKE3
# - Eigen3::Eigen  
# - HNSW::HNSW
# - PostGIS::PostGIS
# - PostgreSQL::LibPQ
# - tree-sitter::tree-sitter
# - nlohmann_json::nlohmann_json
# - Spectra::Spectra
# - MKL::MKL
