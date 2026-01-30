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
#  TARGET
# ==============================================================================
add_library(engine SHARED ${ENGINE_SOURCES} ${ENGINE_HEADERS})
add_library(Hartonomous::Engine ALIAS engine)

set_target_properties(engine PROPERTIES OUTPUT_NAME "engine")
if(MSVC)
    target_compile_definitions(engine PRIVATE "HARTONOMOUS_EXPORT")
endif()

# ==============================================================================
#  INCLUDES
# ==============================================================================
target_include_directories(engine
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        ${CMAKE_CURRENT_SOURCE_DIR}/external/hilbert
        ${CMAKE_CURRENT_SOURCE_DIR}/external/xoshiro
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

# ==============================================================================
#  LINKING
# ==============================================================================

# Find PostgreSQL client library (libpq)
# Help CMake find PostgreSQL on Windows
if(WIN32)
    # Find the PostgreSQL installation
    set(PG_VERSIONS 18 17 16 15 14 13)
    foreach(PG_VER ${PG_VERSIONS})
        if(EXISTS "C:/Program Files/PostgreSQL/${PG_VER}/include")
            set(PostgreSQL_ROOT "C:/Program Files/PostgreSQL/${PG_VER}")
            break()
        endif()
    endforeach()
endif()

find_package(PostgreSQL REQUIRED)

# Explicitly link all required libraries.
# - Spectra pulls in Eigen (which pulls in MKL)
# - HNSW is now standalone (as it should be)
# - BLAKE3 is standalone
# - PostgreSQL for database connection
# Link dependencies
# NOTE: Some dependencies must be PUBLIC because they're used in public headers
# that are included by PostgresExtension:
#   - BLAKE3::BLAKE3: blake3_pipeline.hpp includes blake3.h
#   - Eigen3::Eigen: geometry headers (hopf_fibration.hpp, etc.) include Eigen
#   - HNSW::HNSW: may be used in spatial headers
target_link_libraries(engine
    PUBLIC
        BLAKE3::BLAKE3  # PUBLIC: blake3_pipeline.hpp includes blake3.h
        Eigen3::Eigen   # PUBLIC: geometry headers include Eigen
        HNSW::HNSW      # PUBLIC: spatial headers may use HNSW
    PRIVATE
        Spectra::Spectra
        MKL::MKL
        PostgreSQL::PostgreSQL
        nlohmann_json::nlohmann_json
)

# ==============================================================================
#  COGNITIVE & AI OPS
# ==============================================================================
file(GLOB_RECURSE COGNITIVE_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/cognitive/*.cpp"
)

list(APPEND ENGINE_SOURCES ${COGNITIVE_SOURCES})

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

# ==================== WARNING FLAGS ====================
if(MSVC)
    target_compile_options(engine PRIVATE
        /W4          # Warning level 4
        /WX-         # Don't treat warnings as errors
    )
else()
    target_compile_options(engine PRIVATE
        -Wall -Wextra -Wpedantic
    )
endif()

# ==================== OPTIMIZATION FLAGS ====================
if(MSVC)
    target_compile_options(engine PRIVATE
        $<$<CONFIG:Release>:/O2>       # Maximum optimization
        $<$<CONFIG:Release>:/Oi>       # Enable intrinsic functions
        $<$<CONFIG:Release>:/Ot>       # Favor fast code
        $<$<CONFIG:Release>:/GL>       # Whole program optimization
        $<$<CONFIG:Release>:/fp:fast>  # Fast floating-point model
    )

    # Enable latest SIMD instruction set (AVX-512 with fallback)
    # This allows the compiler to auto-vectorize loops
    target_compile_options(engine PRIVATE
        $<$<CONFIG:Release>:/arch:AVX512>
    )

    # Link-time code generation for Release builds
    target_link_options(engine PRIVATE
        $<$<CONFIG:Release>:/LTCG>     # Link-time code generation
    )
else()
    # GCC/Clang optimizations
    target_compile_options(engine PRIVATE
        $<$<CONFIG:Release>:-O3>           # Maximum optimization
        $<$<CONFIG:Release>:-march=native> # Native CPU architecture
        $<$<CONFIG:Release>:-mtune=native> # Tune for native CPU
        $<$<CONFIG:Release>:-ffast-math>   # Fast math (non-IEEE)
        $<$<CONFIG:Release>:-funroll-loops> # Unroll loops
        $<$<CONFIG:Release>:-fomit-frame-pointer> # Omit frame pointer
    )
endif()

# ==================== LINK-TIME OPTIMIZATION (LTO/IPO) ====================
include(CheckIPOSupported)
check_ipo_supported(RESULT ipo_supported OUTPUT ipo_error)
if(ipo_supported)
    set_property(TARGET engine PROPERTY INTERPROCEDURAL_OPTIMIZATION TRUE)
    message(STATUS "Engine: IPO/LTO enabled")
else()
    message(STATUS "Engine: IPO/LTO not supported: ${ipo_error}")
endif()

# ==================== DEBUG OPTIMIZATIONS ====================
if(MSVC)
    target_compile_options(engine PRIVATE
        $<$<CONFIG:Debug>:/Od>    # Disable optimization
        $<$<CONFIG:Debug>:/Zi>    # Debug information
        $<$<CONFIG:Debug>:/RTC1>  # Runtime checks
    )
else()
    target_compile_options(engine PRIVATE
        $<$<CONFIG:Debug>:-O0>    # No optimization
        $<$<CONFIG:Debug>:-g3>    # Full debug info
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
