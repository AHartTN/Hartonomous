# ==============================================================================
#  DEPENDENCY LOADER
# ==============================================================================

set(CONFIG_DIR ${CMAKE_CURRENT_LIST_DIR})

include(${CONFIG_DIR}/json-config.cmake)
include(${CONFIG_DIR}/blake3-config.cmake)
include(${CONFIG_DIR}/mkl-config.cmake)
include(${CONFIG_DIR}/eigen-config.cmake)
include(${CONFIG_DIR}/hnsw-config.cmake)
include(${CONFIG_DIR}/spectra-config.cmake)

message(STATUS "Hartonomous Dependencies Loaded: nlohmann_json::nlohmann_json, BLAKE3::BLAKE3, MKL::MKL, Eigen3::Eigen, HNSW::HNSW, Spectra::Spectra")