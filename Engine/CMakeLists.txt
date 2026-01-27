cmake_minimum_required(VERSION 3.25)

project(HartonomousEngine LANGUAGES C CXX)

# Engine-wide settings
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Load unified dependency loader
include(${CMAKE_SOURCE_DIR}/cmake/FindHartonomousDeps.cmake)

# Engine sources
file(GLOB_RECURSE ENGINE_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.c"
)

add_library(engine STATIC ${ENGINE_SOURCES})

target_include_directories(engine
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# Link all dependencies
target_link_libraries(engine
    PUBLIC
        Eigen3::Eigen
        Spectra::Spectra
        HNSW::HNSW
        BLAKE3::BLAKE3
        MKL::MKL
)

# Optional: enable IPO/LTO if supported
include(CheckIPOSupported)
check_ipo_supported(RESULT ipo_supported OUTPUT ipo_error)
if(ipo_supported)
    set_property(TARGET engine PROPERTY INTERPROCEDURAL_OPTIMIZATION TRUE)
endif()