# ==============================================================================
#  MKL CONFIGURATION (IMPORTED - HIGH PERFORMANCE)
# ==============================================================================
#
#  Intel MKL (Math Kernel Library) provides optimized BLAS, LAPACK, FFT, etc.
#
#  Threading Options (set HARTONOMOUS_MKL_THREADING before including):
#    - SEQUENTIAL: Single-threaded (default for simplicity)
#    - INTEL:      Intel's threading layer (best for pure MKL workloads)
#    - TBB:        Intel TBB (best for mixed workloads)
#    - GNU:        GNU OpenMP (if using GCC)
#
#  Interface Layer:
#    - LP64:  32-bit integers (default, standard BLAS/LAPACK)
#    - ILP64: 64-bit integers (for huge matrices > 2^31 elements)
#
# ==============================================================================

# User-configurable options (can be set before including this file)
if(NOT DEFINED HARTONOMOUS_MKL_THREADING)
    # Default to GNU OpenMP threading for parallel MKL operations
    # SEQUENTIAL is single-threaded and destroys performance!
    set(HARTONOMOUS_MKL_THREADING "GNU" CACHE STRING "MKL threading layer: SEQUENTIAL, INTEL, TBB, GNU")
endif()

if(NOT DEFINED HARTONOMOUS_MKL_INTERFACE)
    set(HARTONOMOUS_MKL_INTERFACE "LP64" CACHE STRING "MKL interface: LP64 (32-bit int) or ILP64 (64-bit int)")
endif()

# 1. Determine MKL Root
if(DEFINED ENV{MKLROOT})
    set(MKL_ROOT "$ENV{MKLROOT}")
elseif(DEFINED ENV{ONEAPI_ROOT})
    set(MKL_ROOT "$ENV{ONEAPI_ROOT}/mkl/latest")
else()
    if(WIN32)
        set(MKL_ROOT "C:/Program Files (x86)/Intel/oneAPI/mkl/latest")
    elseif(UNIX)
        # Try standard locations for Linux
        if(EXISTS "/opt/intel/oneapi/mkl/latest")
            set(MKL_ROOT "/opt/intel/oneapi/mkl/latest")
        elseif(EXISTS "/usr/include/mkl")
            # Package manager installation (apt install intel-mkl)
            set(MKL_ROOT "/usr")
            set(MKL_IS_SYSTEM_INSTALL TRUE)
        else()
            # Default fallback
            set(MKL_ROOT "/opt/intel/oneapi/mkl/latest")
        endif()
    endif()
endif()

if(NOT EXISTS "${MKL_ROOT}")
    message(FATAL_ERROR "MKL root not found at ${MKL_ROOT}. Please set MKLROOT or ONEAPI_ROOT environment variable.")
endif()

# 2. Define MKL::MKL directly as a GLOBAL IMPORTED target
if(NOT TARGET MKL::MKL)
    add_library(MKL::MKL INTERFACE IMPORTED GLOBAL)

    target_include_directories(MKL::MKL INTERFACE
        "${MKL_ROOT}/include"
    )

    # Handle library paths
    if(MKL_IS_SYSTEM_INSTALL)
        # On Debian/Ubuntu, libs are often in x86_64-linux-gnu
        if(EXISTS "${MKL_ROOT}/lib/x86_64-linux-gnu")
            target_link_directories(MKL::MKL INTERFACE "${MKL_ROOT}/lib/x86_64-linux-gnu")
        else()
            target_link_directories(MKL::MKL INTERFACE "${MKL_ROOT}/lib")
        endif()
    else()
        # Intel Installer layout
        target_link_directories(MKL::MKL INTERFACE "${MKL_ROOT}/lib/intel64")
    endif()

    # ==================== INTERFACE LAYER ====================
    if(HARTONOMOUS_MKL_INTERFACE STREQUAL "ILP64")
        set(MKL_INTERFACE_LIB "mkl_intel_ilp64")
        target_compile_definitions(MKL::MKL INTERFACE MKL_ILP64)
        message(STATUS "MKL: Using ILP64 interface (64-bit integers)")
    else()
        set(MKL_INTERFACE_LIB "mkl_intel_lp64")
        target_compile_definitions(MKL::MKL INTERFACE MKL_LP64)
        message(STATUS "MKL: Using LP64 interface (32-bit integers)")
    endif()

    # ==================== THREADING LAYER ====================
    if(HARTONOMOUS_MKL_THREADING STREQUAL "INTEL")
        set(MKL_THREADING_LIB "mkl_intel_thread")
        message(STATUS "MKL: Using Intel threading layer")
        # Need Intel OpenMP runtime
        if(WIN32)
            target_link_libraries(MKL::MKL INTERFACE iomp5md)
        else()
            target_link_libraries(MKL::MKL INTERFACE iomp5)
        endif()
    elseif(HARTONOMOUS_MKL_THREADING STREQUAL "TBB")
        set(MKL_THREADING_LIB "mkl_tbb_thread")
        message(STATUS "MKL: Using TBB threading layer")
        # Need TBB runtime
        target_link_libraries(MKL::MKL INTERFACE tbb)
    elseif(HARTONOMOUS_MKL_THREADING STREQUAL "GNU")
        set(MKL_THREADING_LIB "mkl_gnu_thread")
        message(STATUS "MKL: Using GNU OpenMP threading layer")
        # Need GNU OpenMP runtime
        find_package(OpenMP REQUIRED)
        target_link_libraries(MKL::MKL INTERFACE OpenMP::OpenMP_CXX)
    else()
        # SEQUENTIAL (default)
        set(MKL_THREADING_LIB "mkl_sequential")
        message(STATUS "MKL: Using sequential (single-threaded) mode")
    endif()

    # ==================== LINK MKL LIBRARIES ====================
    if(WIN32)
        # Windows: Use DLL import libraries to match the project's /MD (Dynamic CRT) usage.
        target_link_libraries(MKL::MKL INTERFACE
            ${MKL_INTERFACE_LIB}_dll
            ${MKL_THREADING_LIB}_dll
            mkl_core_dll
        )
    else()
        # Linux: Standard static/dynamic names
        target_link_libraries(MKL::MKL INTERFACE
            ${MKL_INTERFACE_LIB}
            ${MKL_THREADING_LIB}
            mkl_core
        )
    endif()

    # ==================== SYSTEM DEPENDENCIES ====================
    if(UNIX)
        target_link_libraries(MKL::MKL INTERFACE pthread m dl)
    endif()

    # ==================== PERFORMANCE DEFINES ====================
    target_compile_definitions(MKL::MKL INTERFACE
        # Use MKL's CBLAS interface
        HAVE_CBLAS=1
        # Use MKL's LAPACK interface
        HAVE_LAPACK=1
    )

    message(STATUS "MKL: Configuration complete (${HARTONOMOUS_MKL_INTERFACE}, ${HARTONOMOUS_MKL_THREADING})")
endif()