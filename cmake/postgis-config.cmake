# cmake/postgis-config.cmake

get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
set(POSTGIS_SUBMODULE_PATH "${PROJECT_ROOT}/Engine/external/postgis")

# Check if submodule exists first (preferred for Hartonomous)
if(EXISTS "${POSTGIS_SUBMODULE_PATH}/liblwgeom")
    # Use submodule - look for built library
    find_path(PostGIS_INCLUDE_DIR
        NAMES liblwgeom.h
        PATHS "${POSTGIS_SUBMODULE_PATH}/liblwgeom"
        NO_DEFAULT_PATH
    )

    find_library(PostGIS_LIBRARY
        NAMES lwgeom liblwgeom
        PATHS 
            "${POSTGIS_SUBMODULE_PATH}/liblwgeom/.libs"
            "${POSTGIS_SUBMODULE_PATH}/liblwgeom"
        NO_DEFAULT_PATH
    )

    # Dependencies for static build
    find_package(PkgConfig QUIET REQUIRED)
    pkg_check_modules(GEOS REQUIRED geos)
    pkg_check_modules(PROJ REQUIRED proj)

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
        return()
    endif()
endif()

# Fallback: Try system package
find_package(PkgConfig QUIET REQUIRED)
pkg_check_modules(PostGIS IMPORTED_TARGET liblwgeom)

if(TARGET PkgConfig::PostGIS)
    message(STATUS "PostGIS: Found system liblwgeom")
    add_library(PostGIS::PostGIS ALIAS PkgConfig::PostGIS)
    return()
endif()

# Neither found
message(FATAL_ERROR "PostGIS (liblwgeom) not found. Please install liblwgeom-dev or build the submodule.")