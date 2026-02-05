# cmake/postgis-config.cmake

# 1. Try PkgConfig first (Standard System Install)
find_package(PkgConfig REQUIRED)
pkg_check_modules(PostGIS IMPORTED_TARGET liblwgeom)

if(TARGET PkgConfig::PostGIS)
    message(STATUS "PostGIS: Found system liblwgeom")
    # Alias to expected namespaced target
    add_library(PostGIS::PostGIS ALIAS PkgConfig::PostGIS)
    return()
endif()

# 2. Fallback: Submodule Search
#    If this file is in <ProjectRoot>/cmake/, then ../external/postgis is valid.
get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
set(POSTGIS_SUBMODULE_PATH "${PROJECT_ROOT}/Engine/external/postgis")

# Header (liblwgeom.h)
find_path(PostGIS_INCLUDE_DIR
    NAMES liblwgeom.h
    PATHS "${POSTGIS_SUBMODULE_PATH}/liblwgeom"
    NO_DEFAULT_PATH
    NO_CMAKE_ENVIRONMENT_PATH
    NO_CMAKE_SYSTEM_PATH
)

# Library (liblwgeom.a / .so) - Check .libs (libtool) or build root
find_library(PostGIS_LIBRARY
    NAMES lwgeom liblwgeom
    PATHS 
        "${POSTGIS_SUBMODULE_PATH}/liblwgeom/.libs"
        "${POSTGIS_SUBMODULE_PATH}/liblwgeom"
    NO_DEFAULT_PATH
    NO_CMAKE_ENVIRONMENT_PATH
    NO_CMAKE_SYSTEM_PATH
)

# Dependencies for static build
pkg_check_modules(GEOS REQUIRED geos)
pkg_check_modules(PROJ REQUIRED proj)

# Create Target
if(PostGIS_INCLUDE_DIR AND PostGIS_LIBRARY)
    if(NOT TARGET PostGIS::PostGIS)
        add_library(PostGIS::PostGIS UNKNOWN IMPORTED)
        set_target_properties(PostGIS::PostGIS PROPERTIES
            IMPORTED_LOCATION "${PostGIS_LIBRARY}"
            INTERFACE_INCLUDE_DIRECTORIES "${PostGIS_INCLUDE_DIR};${POSTGIS_SUBMODULE_PATH}/libpgcommon"
            INTERFACE_LINK_LIBRARIES "${GEOS_LIBRARIES};${PROJ_LIBRARIES}"
        )
        message(STATUS "PostGIS: Found at ${POSTGIS_SUBMODULE_PATH}")
    endif()
else()
    message(FATAL_ERROR "PostGIS (liblwgeom) not found. Please install liblwgeom-dev or build the submodule.")
endif()