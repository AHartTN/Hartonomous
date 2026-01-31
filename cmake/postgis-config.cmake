# cmake/postgis-config.cmake

# 1. Calculate Root Relative to This File
#    If this file is in <ProjectRoot>/cmake/, then ../external/postgis is valid.
get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
set(POSTGIS_SUBMODULE_PATH "${PROJECT_ROOT}/Engine/external/postgis")

# 2. Header (liblwgeom.h)
find_path(PostGIS_INCLUDE_DIR
    NAMES liblwgeom.h
    PATHS "${POSTGIS_SUBMODULE_PATH}/liblwgeom"
    NO_DEFAULT_PATH
    NO_CMAKE_ENVIRONMENT_PATH
    NO_CMAKE_SYSTEM_PATH
)

# 3. Library (liblwgeom.a / .so)
find_library(PostGIS_LIBRARY
    NAMES lwgeom liblwgeom
    PATHS "${POSTGIS_SUBMODULE_PATH}/liblwgeom/.libs"
    NO_DEFAULT_PATH
    NO_CMAKE_ENVIRONMENT_PATH
    NO_CMAKE_SYSTEM_PATH
)

# 4. Create Target
if(PostGIS_INCLUDE_DIR AND PostGIS_LIBRARY)
    if(NOT TARGET PostGIS::PostGIS)
        add_library(PostGIS::PostGIS UNKNOWN IMPORTED)
        set_target_properties(PostGIS::PostGIS PROPERTIES
            IMPORTED_LOCATION "${PostGIS_LIBRARY}"
            INTERFACE_INCLUDE_DIRECTORIES "${PostGIS_INCLUDE_DIR}"
        )
        message(STATUS "PostGIS: Found at ${POSTGIS_SUBMODULE_PATH}")
    endif()
else()
    message(FATAL_ERROR "PostGIS not found in submodule: ${POSTGIS_SUBMODULE_PATH}")
endif()