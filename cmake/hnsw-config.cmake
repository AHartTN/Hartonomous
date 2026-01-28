# ==============================================================================
#  HNSWLIB CONFIGURATION (IMPORTED - SIMD OPTIMIZED)
# ==============================================================================
#
#  HNSWLib is a header-only C++ library for fast approximate nearest neighbor search.
#  It implements the Hierarchical Navigable Small World (HNSW) algorithm.
#
#  Performance: HNSWLib uses its own SIMD distance calculations (not MKL).
#  We enable the best available SIMD instruction set for maximum throughput.
#
# ==============================================================================

# User-configurable SIMD level (can be overridden)
if(NOT DEFINED HARTONOMOUS_HNSW_SIMD)
    set(HARTONOMOUS_HNSW_SIMD "AUTO" CACHE STRING "HNSWLib SIMD level: AUTO, AVX512, AVX2, SSE")
endif()

get_filename_component(HNSW_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/hnswlib" ABSOLUTE)

if(NOT EXISTS "${HNSW_ROOT}/hnswlib/hnswlib.h")
    message(FATAL_ERROR "HNSWLib not found at ${HNSW_ROOT}")
endif()

if(NOT TARGET HNSW::HNSW)
    add_library(HNSW::HNSW INTERFACE IMPORTED GLOBAL)

    target_include_directories(HNSW::HNSW INTERFACE
        "${HNSW_ROOT}"
    )

    # ==================== SIMD OPTIMIZATION ====================
    # HNSWLib uses SIMD for distance calculations (dot product, L2, etc.)
    # Enable the highest SIMD level supported by the target CPU.

    if(CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|i.86|amd64)")
        set(HNSW_SIMD_FLAGS "")

        if(HARTONOMOUS_HNSW_SIMD STREQUAL "AUTO")
            # Use native CPU architecture (best for local builds)
            if(MSVC)
                # MSVC: Use AVX512 if available, fallback to AVX2
                # Note: MSVC /arch:AVX512 implies AVX2, AVX, SSE, etc.
                set(HNSW_SIMD_FLAGS "/arch:AVX512")
                message(STATUS "HNSWLib: AUTO SIMD → /arch:AVX512 (fallback to best available)")
            else()
                # GCC/Clang: -march=native detects and uses best available
                set(HNSW_SIMD_FLAGS "-march=native")
                message(STATUS "HNSWLib: AUTO SIMD → -march=native")
            endif()
        elseif(HARTONOMOUS_HNSW_SIMD STREQUAL "AVX512")
            if(MSVC)
                set(HNSW_SIMD_FLAGS "/arch:AVX512")
            else()
                set(HNSW_SIMD_FLAGS "-mavx512f -mavx512dq -mavx512vl -mavx512bw")
            endif()
            message(STATUS "HNSWLib: Using AVX-512 SIMD")
        elseif(HARTONOMOUS_HNSW_SIMD STREQUAL "AVX2")
            if(MSVC)
                set(HNSW_SIMD_FLAGS "/arch:AVX2")
            else()
                set(HNSW_SIMD_FLAGS "-mavx2 -mfma")
            endif()
            message(STATUS "HNSWLib: Using AVX2 SIMD")
        elseif(HARTONOMOUS_HNSW_SIMD STREQUAL "SSE")
            if(MSVC)
                # SSE is implicit on x64 for MSVC
                set(HNSW_SIMD_FLAGS "")
            else()
                set(HNSW_SIMD_FLAGS "-msse4.1")
            endif()
            message(STATUS "HNSWLib: Using SSE SIMD")
        endif()

        if(HNSW_SIMD_FLAGS)
            target_compile_options(HNSW::HNSW INTERFACE ${HNSW_SIMD_FLAGS})
        endif()
    else()
        message(STATUS "HNSWLib: Non-x86 architecture, SIMD auto-detection disabled")
    endif()

    # ==================== COMPILER-SPECIFIC OPTIONS ====================
    if(MSVC)
        # Enable intrinsics and aggressive optimization
        target_compile_options(HNSW::HNSW INTERFACE /Oi /Ot)
    else()
        # GCC/Clang: Enable aggressive optimization
        target_compile_options(HNSW::HNSW INTERFACE -O3)
    endif()

    # Note: HNSWLib is standalone. It does NOT depend on Eigen or MKL.
    message(STATUS "HNSWLib: Header-only ANN library configured")
endif()