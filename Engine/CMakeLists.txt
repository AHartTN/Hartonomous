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
add_library(engine STATIC ${ENGINE_SOURCES} ${ENGINE_HEADERS})
add_library(Hartonomous::Engine ALIAS engine)

# ==============================================================================
#  INCLUDES
# ==============================================================================
target_include_directories(engine
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

# ==============================================================================
#  LINKING
# ==============================================================================
# Explicitly link all required libraries.
# - Spectra pulls in Eigen (which pulls in MKL)
# - HNSW is now standalone (as it should be)
# - BLAKE3 is standalone
target_link_libraries(engine
    PUBLIC
        Spectra::Spectra
        HNSW::HNSW
        BLAKE3::BLAKE3
        Eigen3::Eigen  # Explicitly added back since HNSW doesn't carry it
        MKL::MKL       # Explicitly added back for safety
)

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