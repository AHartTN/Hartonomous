# ==============================================================================
#  DEPENDENCY LOADER
# ==============================================================================

set(CONFIG_DIR ${CMAKE_CURRENT_LIST_DIR})

include(${CONFIG_DIR}/mkl-config.cmake)
include(${CONFIG_DIR}/eigen-config.cmake)
include(${CONFIG_DIR}/spectra-config.cmake)
include(${CONFIG_DIR}/hnsw-config.cmake)
include(${CONFIG_DIR}/blake3-config.cmake)

message(STATUS "Hartonomous Dependencies Loaded: MKL::MKL, Eigen3::Eigen, Spectra::Spectra, HNSW::HNSW, BLAKE3::BLAKE3")