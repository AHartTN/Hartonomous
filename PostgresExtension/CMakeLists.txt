cmake_minimum_required(VERSION 3.25)

project(HartonomousPostgresExt LANGUAGES C CXX)

# ==============================================================================
#  1. AUTO-DETECT POSTGRESQL (Via pg_config)
# ==============================================================================

# Find pg_config in the system path (e.g. /usr/bin/pg_config)
find_program(PG_CONFIG NAMES pg_config REQUIRED)

# Query authoritative paths
execute_process(COMMAND ${PG_CONFIG} --includedir-server OUTPUT_VARIABLE PG_SERVER_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
execute_process(COMMAND ${PG_CONFIG} --pkglibdir         OUTPUT_VARIABLE PG_PKGLIBDIR        OUTPUT_STRIP_TRAILING_WHITESPACE)

message(STATUS "PostgreSQL Found:")
message(STATUS "  Config:   ${PG_CONFIG}")
message(STATUS "  Include:  ${PG_SERVER_INCLUDE_DIR}")
message(STATUS "  Install:  ${PG_PKGLIBDIR}")

# ==============================================================================
#  2. FIND POSTGIS (Robust Loader)
# ==============================================================================

# A. Check if target exists (from root build)
if(TARGET PostGIS::PostGIS)
    message(STATUS "PostGIS: Target inherited from main build.")

# B. Fallback: Load manually from parent cmake folder
else()
    # Resolve the path to <Root>/cmake
    get_filename_component(HARTONOMOUS_CMAKE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../cmake" ABSOLUTE)
    set(POSTGIS_CONFIG_FILE "${HARTONOMOUS_CMAKE_DIR}/postgis-config.cmake")

    message(STATUS "PostGIS: Target not found, loading: ${POSTGIS_CONFIG_FILE}")

    if(EXISTS "${POSTGIS_CONFIG_FILE}")
        include("${POSTGIS_CONFIG_FILE}")
    else()
        message(FATAL_ERROR "Critical: Could not find '${POSTGIS_CONFIG_FILE}'")
    endif()
endif()

# ==============================================================================
#  3. TARGET DEFINITION
# ==============================================================================

set(EXTENSION_NAME "hartonomous")

file(GLOB_RECURSE EXT_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.c"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp"
)

add_library(${EXTENSION_NAME} MODULE ${EXT_SOURCES})

# ==============================================================================
#  4. INCLUDES & DEPENDENCIES
# ==============================================================================

target_include_directories(${EXTENSION_NAME}
    PRIVATE
        ${PG_SERVER_INCLUDE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_SOURCE_DIR}/Engine/include
)

target_link_libraries(${EXTENSION_NAME}
    PRIVATE
        Hartonomous::Engine
        PostGIS::PostGIS
)

# ==============================================================================
#  5. CONFIGURATION
# ==============================================================================

# Remove "lib" prefix to match Postgres extension naming conventions
set_target_properties(${EXTENSION_NAME} PROPERTIES
    PREFIX ""
    SUFFIX ".so"
)

if(MSVC)
    target_compile_definitions(${EXTENSION_NAME} PRIVATE "DLLEXPORT=__declspec(dllexport)" "WIN32" "_WIN32")
    target_link_options(${EXTENSION_NAME} PRIVATE "/NODEFAULTLIB:LIBCMT")
endif()

# ==============================================================================
#  6. INSTALLATION
# ==============================================================================

# Installs to the correct directory detected by pg_config
install(TARGETS ${EXTENSION_NAME}
    LIBRARY DESTINATION "${PG_PKGLIBDIR}"
)