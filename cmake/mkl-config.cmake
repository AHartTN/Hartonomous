# MKL root directory (system install or vendored)
set(MKL_ROOT "C:/Program Files (x86)/Intel/oneAPI/mkl/latest" CACHE PATH "MKL root directory")

# Create a simple INTERFACE target (no namespaces)
add_library(MKL INTERFACE)

# Include directory
target_include_directories(MKL INTERFACE
    "${MKL_ROOT}/include"
)

# Library directory
target_link_directories(MKL INTERFACE
    "${MKL_ROOT}/lib/intel64"
)

# Link MKL components
target_link_libraries(MKL INTERFACE
    mkl_intel_lp64
    mkl_sequential
    mkl_core
)
