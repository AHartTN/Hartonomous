# ==============================================================================
#  DEPENDENCY LOADER
# ==============================================================================

set(CONFIG_DIR ${CMAKE_CURRENT_LIST_DIR})

include(${CONFIG_DIR}/blake3-config.cmake)
include(${CONFIG_DIR}/mkl-config.cmake)
include(${CONFIG_DIR}/eigen-config.cmake)
include(${CONFIG_DIR}/hnsw-config.cmake)
include(${CONFIG_DIR}/spectra-config.cmake)
include(${CONFIG_DIR}/json-config.cmake)
include(${CONFIG_DIR}/postgres-config.cmake)
include(${CONFIG_DIR}/postgis-config.cmake)
include(${CONFIG_DIR}/treesitter-config.cmake)
#include(${CONFIG_DIR}/hilbert-config.cmake)


message(STATUS "Hartonomous Dependencies Loaded: BLAKE3::BLAKE3, MKL::MKL, Eigen3::Eigen, HNSW::HNSW, Spectra::Spectra, nlohmann_json::nlohmann_json, PostGIS::PostGIS, tree-sitter::tree-sitter, Hilbert::Hilbert")