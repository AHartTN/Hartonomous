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
                /arch:AVX512             # AVX-512 instructions
            >
        )
        target_link_options(${target} PRIVATE
            $<$<CONFIG:Release>:/LTCG>   # Link-time code generation
        )
    else()
        # GCC/Clang flags
        target_compile_options(${target} PRIVATE
            -Wall                        # All warnings
            -Wextra                      # Extra warnings
            -Wpedantic                   # Pedantic warnings
            -march=native               # SIMD support required by Eigen/HNSW
            $<$<CONFIG:Release>:
                -O3                      # Maximum optimization
                -mtune=native            # Tune for this CPU
                -ffast-math              # Fast floating point
                -funroll-loops           # Unroll loops
                -fomit-frame-pointer     # Omit frame pointer
            >
            $<$<CONFIG:Debug>:
                -O0                      # No optimization for debugging
                -g                       # Debug symbols
            >
        )
    endif()
    
    # Interprocedural Optimization (IPO/LTO) — Release only
    if(NOT CMAKE_BUILD_TYPE STREQUAL "Debug")
        include(CheckIPOSupported)
        check_ipo_supported(RESULT ipo_supported OUTPUT ipo_output)
        if(ipo_supported)
            set_property(TARGET ${target} PROPERTY INTERPROCEDURAL_OPTIMIZATION TRUE)
            message(STATUS "IPO/LTO enabled for ${target}")
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
