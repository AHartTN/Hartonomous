cmake_minimum_required(VERSION 3.22)

project(HartonomousEngine LANGUAGES C CXX)

# ==============================================================================
#  DEPENDENCIES
# ==============================================================================
find_package(OpenMP REQUIRED)

# ==============================================================================
#  SOURCE CLASSIFICATION
# ==============================================================================

# --- CORE (Math, Geometry, Hashing, ML) ---
# NO Database dependencies allowed here.
file(GLOB_RECURSE CORE_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/geometry/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/hashing/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/ml/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/unicode/*.cpp"
)
# Exclude ingestor from unicode core
list(FILTER CORE_SOURCES EXCLUDE REGEX "src/unicode/ingestor/.*")

# --- IO (Database, Ingestion, Cognitive) ---
file(GLOB_RECURSE IO_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/database/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/ingestion/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/query/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/cognitive/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/storage/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/interop_api.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/unicode/ingestor/*.cpp"
)

file(GLOB_RECURSE ENGINE_HEADERS
    "${CMAKE_CURRENT_SOURCE_DIR}/include/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/include/*.hpp"
)

# ==============================================================================
#  TARGET: ENGINE CORE (Geometry/Math)
# ==============================================================================
add_library(engine_core SHARED ${CORE_SOURCES} ${ENGINE_HEADERS})
add_library(Hartonomous::Core ALIAS engine_core)

set_target_properties(engine_core PROPERTIES OUTPUT_NAME "engine_core")
target_compile_definitions(engine_core PRIVATE HARTONOMOUS_EXPORT)

target_include_directories(engine_core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        ${CMAKE_CURRENT_SOURCE_DIR}/external/hilbert
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_link_libraries(engine_core
    PUBLIC
        BLAKE3::BLAKE3
        Eigen3::Eigen
        HNSW::HNSW
        OpenMP::OpenMP_CXX
    PRIVATE
        Spectra::Spectra
        MKL::MKL
)

# ==============================================================================
#  TARGET: ENGINE IO (Database/Ingestion)
# ==============================================================================
add_library(engine_io SHARED ${IO_SOURCES} ${ENGINE_HEADERS})
add_library(Hartonomous::IO ALIAS engine_io)
# Alias "engine" to the full stack for tools
add_library(engine ALIAS engine_io) 

set_target_properties(engine_io PROPERTIES OUTPUT_NAME "engine_io")
target_compile_definitions(engine_io PRIVATE HARTONOMOUS_EXPORT)

target_include_directories(engine_io
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_link_libraries(engine_io
    PUBLIC
        engine_core
        PostGIS::PostGIS
        PostgreSQL::LibPQ
        tree-sitter::tree-sitter
        nlohmann_json::nlohmann_json
)

# ==============================================================================
#  COMPILER OPTIONS (Shared)
# ==============================================================================
function(configure_optimization target)
    if(MSVC)
        target_compile_options(${target} PRIVATE
            /W4 /WX- $<$<CONFIG:Release>:/O2 /Oi /Ot /GL /fp:fast /arch:AVX512>
        )
        target_link_options(${target} PRIVATE $<$<CONFIG:Release>:/LTCG>)
    else()
        target_compile_options(${target} PRIVATE
            -Wall -Wextra -Wpedantic
            $<$<CONFIG:Release>:-O3 -march=native -mtune=native -ffast-math -funroll-loops -fomit-frame-pointer>
        )
    endif()

    include(CheckIPOSupported)
    check_ipo_supported(RESULT ipo_supported)
    if(ipo_supported)
        set_property(TARGET ${target} PROPERTY INTERPROCEDURAL_OPTIMIZATION TRUE)
    endif()
endfunction()

configure_optimization(engine_core)
configure_optimization(engine_io)

# ==============================================================================
#  TOOLS & TESTS
# ==============================================================================
add_subdirectory(tools)
enable_testing()
add_subdirectory(tests)

# ==============================================================================
#  INSTALLATION
# ==============================================================================
include(GNUInstallDirs)

install(TARGETS engine_core engine_io
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

install(DIRECTORY include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}/hartonomous)