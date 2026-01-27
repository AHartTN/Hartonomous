# ==============================================================================
#  SPECTRA CONFIGURATION (IMPORTED)
# ==============================================================================

get_filename_component(SPECTRA_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/spectra" ABSOLUTE)

if(NOT EXISTS "${SPECTRA_ROOT}/include/Spectra/SymEigsSolver.h")
    message(FATAL_ERROR "Spectra not found at ${SPECTRA_ROOT}")
endif()

if(NOT TARGET Spectra::Spectra)
    add_library(Spectra::Spectra INTERFACE IMPORTED GLOBAL)

    target_include_directories(Spectra::Spectra INTERFACE
        "${SPECTRA_ROOT}/include"
    )

    # STRICT DEPENDENCY: Spectra relies on Eigen
    target_link_libraries(Spectra::Spectra INTERFACE
        Eigen3::Eigen
    )
endif()