cmake_minimum_required(VERSION 3.25)

project(HartonomousEngine LANGUAGES C CXX)

# ==============================================================================
#  SOURCES
# ==============================================================================
file(GLOB_RECURSE ENGINE_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.c"
)

file(GLOB_RECURSE ENGINE_HEADERS
    "${CMAKE_CURRENT_SOURCE_DIR}/include/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/include/*.hpp"
)

# ==============================================================================
#  TARGET (PURE COMPUTE ENGINE)
# ==============================================================================
add_library(engine SHARED ${ENGINE_SOURCES} ${ENGINE_HEADERS})
add_library(Hartonomous::Engine ALIAS engine)

set_target_properties(engine PROPERTIES OUTPUT_NAME "engine")

if(MSVC)
    target_compile_definitions(engine PRIVATE HARTONOMOUS_EXPORT)
endif()

# ==============================================================================
#  INCLUDE PATHS (EXPLICIT, ZERO-GUESSING)
# ==============================================================================
target_include_directories(engine
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        ${CMAKE_CURRENT_SOURCE_DIR}/external/hilbert
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

# ==============================================================================
#  LINKING (NO POSTGRES, NO DISCOVERY)
# ==============================================================================
#
# The Engine is pure C++ compute. It must NOT:
#   - link libpq
#   - depend on PostgreSQL
#   - depend on pg_config
#   - depend on server headers
#
# The Postgres extension links *to the engine*, not the other way around.
#
target_link_libraries(engine
    PUBLIC
        BLAKE3::BLAKE3
        Eigen3::Eigen
        HNSW::HNSW
        tree-sitter::tree-sitter
        PostGIS::PostGIS
        PostgreSQL::LibPQ
    PRIVATE
        Spectra::Spectra
        MKL::MKL
        nlohmann_json::nlohmann_json
)


# ==============================================================================
#  COGNITIVE / AI OPS
# ==============================================================================
file(GLOB_RECURSE COGNITIVE_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/cognitive/*.cpp"
)

target_sources(engine PRIVATE ${COGNITIVE_SOURCES})

# ==============================================================================
#  TOOLS
# ==============================================================================
add_subdirectory(tools)

# ==============================================================================
#  TESTING
# ==============================================================================
enable_testing()
add_subdirectory(tests)

# ==============================================================================
#  COMPILER OPTIONS (HIGH PERFORMANCE)
# ==============================================================================
if(MSVC)
    target_compile_options(engine PRIVATE
        /W4
        /WX-
        $<$<CONFIG:Release>:/O2>
        $<$<CONFIG:Release>:/Oi>
        $<$<CONFIG:Release>:/Ot>
        $<$<CONFIG:Release>:/GL>
        $<$<CONFIG:Release>:/fp:fast>
        $<$<CONFIG:Release>:/arch:AVX512>
    )
    target_link_options(engine PRIVATE
        $<$<CONFIG:Release>:/LTCG>
    )
else()
    target_compile_options(engine PRIVATE
        -Wall -Wextra -Wpedantic
        $<$<CONFIG:Release>:-O3>
        $<$<CONFIG:Release>:-march=native>
        $<$<CONFIG:Release>:-mtune=native>
        $<$<CONFIG:Release>:-ffast-math>
        $<$<CONFIG:Release>:-funroll-loops>
        $<$<CONFIG:Release>:-fomit-frame-pointer>
    )
endif()

# ==============================================================================
#  LTO / IPO
# ==============================================================================
include(CheckIPOSupported)
check_ipo_supported(RESULT ipo_supported OUTPUT ipo_error)
if(ipo_supported)
    set_property(TARGET engine PROPERTY INTERPROCEDURAL_OPTIMIZATION TRUE)
    message(STATUS "Engine: IPO/LTO enabled")
else()
    message(STATUS "Engine: IPO/LTO not supported: ${ipo_error}")
endif()

# ==============================================================================
#  DEBUG OPTIMIZATIONS
# ==============================================================================
if(MSVC)
    target_compile_options(engine PRIVATE
        $<$<CONFIG:Debug>:/Od>
        $<$<CONFIG:Debug>:/Zi>
        $<$<CONFIG:Debug>:/RTC1>
    )
else()
    target_compile_options(engine PRIVATE
        $<$<CONFIG:Debug>:-O0>
        $<$<CONFIG:Debug>:-g3>
    )
endif()

# ==============================================================================
#  INSTALLATION
# ==============================================================================
include(GNUInstallDirs)

install(TARGETS engine
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

install(DIRECTORY include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}/hartonomous)
