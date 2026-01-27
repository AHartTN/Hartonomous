set(SPECTRA_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/spectra" CACHE PATH "Spectra root")

add_library(Spectra INTERFACE)

target_include_directories(Spectra INTERFACE
    "${SPECTRA_ROOT}/include"
)

target_link_libraries(Spectra INTERFACE
    Eigen3
)
