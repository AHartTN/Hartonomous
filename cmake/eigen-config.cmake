set(EIGEN_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/eigen" CACHE PATH "Eigen root")

add_library(Eigen3 INTERFACE)

target_include_directories(Eigen3 INTERFACE
    "${EIGEN_ROOT}"
)

target_compile_definitions(Eigen3 INTERFACE
    EIGEN_USE_MKL_ALL
)

target_link_libraries(Eigen3 INTERFACE
    MKL::MKL
)
