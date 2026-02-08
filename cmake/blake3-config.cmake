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

    # CRITICAL: Make include directory PUBLIC so consumers can find blake3.h
    target_include_directories(blake3_impl PUBLIC
        $<BUILD_INTERFACE:${BLAKE3_ROOT}/c>
        $<INSTALL_INTERFACE:include>
    )

    # ==================== SIMD VARIANTS ====================
    # Use hand-written assembly on Unix x86-64 (faster than C intrinsics).
    # Falls back to C intrinsics on MSVC/other platforms.

    if(CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|amd64)" AND UNIX AND NOT APPLE)
        # Hand-written assembly — upstream default on Linux x86-64
        enable_language(ASM)
        target_sources(blake3_impl PRIVATE
            "${BLAKE3_ROOT}/c/blake3_sse2_x86-64_unix.S"
            "${BLAKE3_ROOT}/c/blake3_sse41_x86-64_unix.S"
            "${BLAKE3_ROOT}/c/blake3_avx2_x86-64_unix.S"
            "${BLAKE3_ROOT}/c/blake3_avx512_x86-64_unix.S"
        )
        target_compile_options(blake3_impl PRIVATE -O3)
    elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|i.86|amd64)")
        # C intrinsics fallback (MSVC, macOS, 32-bit)
        target_sources(blake3_impl PRIVATE "${BLAKE3_ROOT}/c/blake3_sse2.c")
        target_sources(blake3_impl PRIVATE "${BLAKE3_ROOT}/c/blake3_sse41.c")
        target_sources(blake3_impl PRIVATE "${BLAKE3_ROOT}/c/blake3_avx2.c")
        target_sources(blake3_impl PRIVATE "${BLAKE3_ROOT}/c/blake3_avx512.c")

        if(MSVC)
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_sse2.c"
                PROPERTIES COMPILE_FLAGS "/O2")
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_sse41.c"
                PROPERTIES COMPILE_FLAGS "/O2")
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_avx2.c"
                PROPERTIES COMPILE_FLAGS "/O2 /arch:AVX2")
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_avx512.c"
                PROPERTIES COMPILE_FLAGS "/O2 /arch:AVX512")
            target_compile_options(blake3_impl PRIVATE /O2 /Oi)
        else()
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_sse2.c"
                PROPERTIES COMPILE_FLAGS "-O3 -msse2")
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_sse41.c"
                PROPERTIES COMPILE_FLAGS "-O3 -msse4.1")
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_avx2.c"
                PROPERTIES COMPILE_FLAGS "-O3 -mavx2")
            set_source_files_properties("${BLAKE3_ROOT}/c/blake3_avx512.c"
                PROPERTIES COMPILE_FLAGS "-O3 -mavx512f -mavx512vl")
            target_compile_options(blake3_impl PRIVATE -O3)
        endif()
    endif()

    # Alias to the standard namespace
    add_library(BLAKE3::BLAKE3 ALIAS blake3_impl)

    message(STATUS "BLAKE3: Runtime SIMD dispatch (hand-written asm on Linux x64, C intrinsics fallback)")
endif()
