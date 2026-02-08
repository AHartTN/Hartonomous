# ==============================================================================
# Compiler Flags and Optimization Configuration
# ==============================================================================
# Centralized compiler flags for all Hartonomous targets
#
# Usage:
#   include(cmake/utils/CompilerFlags.cmake)
#   apply_hartonomous_compiler_flags(my_target)
# ==============================================================================

function(apply_hartonomous_compiler_flags target)
    # C++20 standard
    set_target_properties(${target} PROPERTIES
        CXX_STANDARD 20
        CXX_STANDARD_REQUIRED ON
        CXX_EXTENSIONS OFF
    )

    # Platform-specific flags
    if(MSVC)
        target_compile_options(${target} PRIVATE
            /W4                          # Warning level 4
            /WX-                         # Warnings not as errors
            $<$<CONFIG:Release>:
                /O2                      # Maximum optimization
                /Oi                      # Enable intrinsic functions
                /Ot                      # Favor fast code
                /GL                      # Whole program optimization
                /fp:fast                 # Fast floating point
                /arch:AVX2               # AVX2 (use AVX512 on 14900KS)
            >
        )
        target_link_options(${target} PRIVATE
            $<$<CONFIG:Release>:/LTCG>   # Link-time code generation
        )
    else()
        # GCC/Clang flags — -march=native auto-detects SIMD (AVX2+FMA on 6850K, AVX-512 on 14900KS)
        target_compile_options(${target} PRIVATE
            -Wall -Wextra -Wpedantic
            -march=native                # Auto-detect: AVX2+FMA on Broadwell, AVX-512 on Raptor Lake
            $<$<CONFIG:Release>:
                -O3                      # Maximum optimization
                -mtune=native            # Tune scheduling for this CPU
                -ffast-math              # FMA fusion, reciprocal approx, reordering
                -funroll-loops           # Unroll tight BLAKE3/Eigen loops
                -fomit-frame-pointer     # Free up RBP for general use
                -ftree-vectorize         # Auto-vectorize (implied by -O3 but be explicit)
                -fvect-cost-model=unlimited  # Vectorize even when cost model says no
                -fprefetch-loop-arrays   # Hardware prefetch in loops (helps 15MB L3)
            >
            $<$<CONFIG:Debug>:
                -O0 -g
            >
        )
    endif()

    # Interprocedural Optimization (IPO/LTO) — Release only
    # Use -flto=auto for parallel LTRANS (GCC default is serial!)
    if(NOT CMAKE_BUILD_TYPE STREQUAL "Debug")
        include(CheckIPOSupported)
        check_ipo_supported(RESULT ipo_supported OUTPUT ipo_output)
        if(ipo_supported)
            set_property(TARGET ${target} PROPERTY INTERPROCEDURAL_OPTIMIZATION TRUE)
            if(NOT MSVC)
                target_compile_options(${target} PRIVATE -flto=auto)
                target_link_options(${target} PRIVATE -flto=auto)
            endif()
            message(STATUS "IPO/LTO enabled for ${target} (parallel)")
        else()
            message(STATUS "IPO/LTO not supported for ${target}: ${ipo_output}")
        endif()
    endif()
endfunction()

# Position Independent Code for shared libraries
function(enable_pic target)
    set_target_properties(${target} PROPERTIES
        POSITION_INDEPENDENT_CODE ON
    )
endfunction()
