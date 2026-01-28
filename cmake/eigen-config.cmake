# ==============================================================================
#  EIGEN CONFIGURATION (IMPORTED - MKL ACCELERATED)
# ==============================================================================
#
#  Eigen is a header-only C++ template library for linear algebra.
#  When paired with Intel MKL, it achieves near-optimal performance.
#
#  Key Optimizations:
#    - EIGEN_USE_MKL_ALL: Use MKL for all supported operations
#    - EIGEN_USE_BLAS/LAPACKE: Explicit BLAS/LAPACK backend selection
#    - Vectorization: AVX2/AVX512 SIMD instructions
#    - Fast math: Relaxed IEEE compliance for speed
#
# ==============================================================================

get_filename_component(EIGEN_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/eigen" ABSOLUTE)

if(NOT EXISTS "${EIGEN_ROOT}/Eigen/Core")
    message(FATAL_ERROR "Eigen3 not found at ${EIGEN_ROOT}")
endif()

if(NOT TARGET Eigen3::Eigen)
    add_library(Eigen3::Eigen INTERFACE IMPORTED GLOBAL)

    target_include_directories(Eigen3::Eigen INTERFACE
        "${EIGEN_ROOT}"
    )

    # ==================== MKL BACKEND ====================
    target_compile_definitions(Eigen3::Eigen INTERFACE
        # Use MKL for ALL supported operations (BLAS, LAPACK, VML, etc.)
        EIGEN_USE_MKL_ALL

        # Explicit BLAS backend (redundant with EIGEN_USE_MKL_ALL but explicit)
        EIGEN_USE_BLAS

        # Use MKL's LAPACK for eigenvalue decomposition, SVD, etc.
        EIGEN_USE_LAPACKE
    )

    # ==================== VECTORIZATION ====================
    # Detect available SIMD and enable corresponding Eigen flags
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|i.86|amd64)")
        # Check for AVX512 support (we'll detect this at compile time)
        target_compile_definitions(Eigen3::Eigen INTERFACE
            # Enable explicit vectorization (Eigen will use SSE/AVX/AVX512)
            EIGEN_VECTORIZE

            # Let Eigen detect the best available instruction set
            # (Will use AVX512 if compiled with /arch:AVX512 or -march=native)
            EIGEN_VECTORIZE_AVX2
            EIGEN_VECTORIZE_FMA
        )

        # Note: EIGEN_VECTORIZE_AVX512 is automatically enabled when
        # the compiler has AVX-512 flags (-march=native or /arch:AVX512)
    endif()

    # ==================== PERFORMANCE TUNING ====================
    target_compile_definitions(Eigen3::Eigen INTERFACE
        # Allow Eigen to use fast math (non-IEEE compliant for speed)
        # Only enable if your use case can tolerate slightly different results
        # EIGEN_FAST_MATH

        # Disable debug checks in Release builds
        $<$<CONFIG:Release>:EIGEN_NO_DEBUG>
        $<$<CONFIG:Release>:EIGEN_NO_STATIC_ASSERT>

        # Enable parallelization via MKL threading
        EIGEN_USE_THREADS
    )

    # ==================== ALIGNMENT ====================
    # Eigen benefits from aligned memory (16-byte for SSE, 32-byte for AVX, 64-byte for AVX512)
    # This is handled automatically but we can hint to the compiler
    if(MSVC)
        target_compile_options(Eigen3::Eigen INTERFACE /Zc:__cplusplus)
    endif()

    # ==================== DEPENDENCY ====================
    # STRICT DEPENDENCY: Eigen relies on MKL for BLAS/LAPACK backend
    target_link_libraries(Eigen3::Eigen INTERFACE
        MKL::MKL
    )

    message(STATUS "Eigen: MKL-accelerated with BLAS/LAPACK/VML backend + SIMD vectorization")
endif()