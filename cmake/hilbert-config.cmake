# cmake/hilbert-config.cmake

# 1. Calculate Root
get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
set(HILBERT_SUBMODULE_PATH "${PROJECT_ROOT}/Engine/external/hilbert")

# 2. Header
find_path(Hilbert_INCLUDE_DIR
    NAMES hilbert.h
    PATHS "${HILBERT_SUBMODULE_PATH}" "${HILBERT_SUBMODULE_PATH}/include"
    NO_DEFAULT_PATH
    NO_CMAKE_ENVIRONMENT_PATH
    NO_CMAKE_SYSTEM_PATH
)

# 3. Create Target (Header Only assumption, usually)
if(Hilbert_INCLUDE_DIR)
    if(NOT TARGET Hilbert::Hilbert)
        add_library(Hilbert::Hilbert INTERFACE IMPORTED)
        set_target_properties(Hilbert::Hilbert PROPERTIES
            INTERFACE_INCLUDE_DIRECTORIES "${Hilbert_INCLUDE_DIR}"
        )
        message(STATUS "Hilbert: Found at ${HILBERT_SUBMODULE_PATH}")
    endif()
else()
    message(FATAL_ERROR "Hilbert not found in submodule: ${HILBERT_SUBMODULE_PATH}")
endif()