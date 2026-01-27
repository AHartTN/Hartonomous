# ==============================================================================
#  BLAKE3 CONFIGURATION (BUILT FROM SOURCE - SIMD OPTIMIZED)
# ==============================================================================
#
#  BLAKE3 uses runtime CPU dispatch to select the fastest implementation.
#  Each SIMD variant MUST be compiled with its specific intrinsics flags
#  to ensure proper vectorization.
#
# ==============================================================================

get_filename_component(BLAKE3_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/blake3" ABSOLUTE)

if(NOT EXISTS "${BLAKE3_ROOT}/c/blake3.c")
    message(FATAL_ERROR "BLAKE3 not found at ${BLAKE3_ROOT}")
endif()

if(NOT TARGET BLAKE3::BLAKE3)
    # Core and portable implementations
    add_library(blake3_impl STATIC
        "${BLAKE3_ROOT}/c/blake3.c"
        "${BLAKE3_ROOT}/c/blake3_dispatch.c"
        "${BLAKE3_ROOT}/c/blake3_portable.c"
    )

    set_target_properties(blake3_impl PROPERTIES POSITION_INDEPENDENT_CODE ON)

    target_include_directories(blake3_impl PUBLIC
        "${BLAKE3_ROOT}/c"
    )

    # ==================== SIMD VARIANTS ====================
    # Each SIMD file is compiled separately with the correct flags
    # to ensure the compiler generates proper vectorized code.

    # SSE2 (baseline for x64)
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|i.86|amd64)")
        target_sources(blake3_impl PRIVATE "${BLAKE3_ROOT}/c/blake3_sse2.c")
        if(MSVC)
            # MSVC: SSE2 is implicit on x64
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_sse2.c"
                PROPERTIES COMPILE_FLAGS "/O2")
        else()
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_sse2.c"
                PROPERTIES COMPILE_FLAGS "-O3 -msse2")
        endif()
    endif()

    # SSE4.1
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|i.86|amd64)")
        target_sources(blake3_impl PRIVATE "${BLAKE3_ROOT}/c/blake3_sse41.c")
        if(MSVC)
            # MSVC doesn't have /arch:SSE4.1 - it's implied by later archs
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_sse41.c"
                PROPERTIES COMPILE_FLAGS "/O2")
        else()
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_sse41.c"
                PROPERTIES COMPILE_FLAGS "-O3 -msse4.1")
        endif()
    endif()

    # AVX2
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|i.86|amd64)")
        target_sources(blake3_impl PRIVATE "${BLAKE3_ROOT}/c/blake3_avx2.c")
        if(MSVC)
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_avx2.c"
                PROPERTIES COMPILE_FLAGS "/O2 /arch:AVX2")
        else()
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_avx2.c"
                PROPERTIES COMPILE_FLAGS "-O3 -mavx2")
        endif()
    endif()

    # AVX512
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|i.86|amd64)")
        target_sources(blake3_impl PRIVATE "${BLAKE3_ROOT}/c/blake3_avx512.c")
        if(MSVC)
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_avx512.c"
                PROPERTIES COMPILE_FLAGS "/O2 /arch:AVX512")
        else()
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_avx512.c"
                PROPERTIES COMPILE_FLAGS "-O3 -mavx512f -mavx512vl")
        endif()
    endif()

    # Base optimization for non-SIMD files
    if(MSVC)
        target_compile_options(blake3_impl PRIVATE /O2 /Oi)
        # /Oi = enable intrinsic functions
    else()
        target_compile_options(blake3_impl PRIVATE -O3)
    endif()

    # Alias to the standard namespace
    add_library(BLAKE3::BLAKE3 ALIAS blake3_impl)

    message(STATUS "BLAKE3: Runtime SIMD dispatch enabled (SSE2/SSE4.1/AVX2/AVX512)")
endif()