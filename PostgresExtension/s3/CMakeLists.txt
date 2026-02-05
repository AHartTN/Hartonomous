cmake_minimum_required(VERSION 3.22)

project(S3PostgresExt LANGUAGES CXX)

# ==============================================================================
#  1. AUTO-DETECT POSTGRESQL & POSTGIS
# ==============================================================================

find_program(PG_CONFIG NAMES pg_config REQUIRED)

execute_process(COMMAND ${PG_CONFIG} --includedir-server OUTPUT_VARIABLE PG_SERVER_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
execute_process(COMMAND ${PG_CONFIG} --pkglibdir         OUTPUT_VARIABLE PG_PKGLIBDIR        OUTPUT_STRIP_TRAILING_WHITESPACE)
execute_process(COMMAND ${PG_CONFIG} --sharedir          OUTPUT_VARIABLE PG_SHAREDIR         OUTPUT_STRIP_TRAILING_WHITESPACE)

message(STATUS "PostgreSQL Found: ${PG_CONFIG}")

# Reuse the PostGIS logic from root cmake/postgis-config.cmake
# find_package(PostGIS REQUIRED) <-- REMOVED: Inherited from root

# ==============================================================================
#  2. TARGET DEFINITION
# ==============================================================================

set(EXTENSION_NAME "s3")

file(GLOB_RECURSE EXT_SOURCES
    "src/*.cpp"
)

add_library(${EXTENSION_NAME} MODULE ${EXT_SOURCES})

# ==============================================================================
#  3. INCLUDES & DEPENDENCIES
# ==============================================================================

target_include_directories(${EXTENSION_NAME}
    PRIVATE
        ${PG_SERVER_INCLUDE_DIR}
        ${CMAKE_SOURCE_DIR}/Engine/include
        ${CMAKE_SOURCE_DIR}/Engine/external/eigen
)

# Link against Engine Core (Math/Geometry) AND PostGIS (for headers/symbols)
target_link_libraries(${EXTENSION_NAME}
    PRIVATE
        engine_core
        PostGIS::PostGIS
)

# ==============================================================================
#  4. CONFIGURATION
# ==============================================================================

set_target_properties(${EXTENSION_NAME} PROPERTIES
    PREFIX ""
    SUFFIX ".so"
)

# ==============================================================================
#  5. INSTALLATION
# ==============================================================================

# Install extension library to local install/ and system PostgreSQL
install(TARGETS ${EXTENSION_NAME}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
)

install(TARGETS ${EXTENSION_NAME}
    LIBRARY DESTINATION "${PG_PKGLIBDIR}"
)

# Install extension SQL and control files to local install/ and system PostgreSQL
install(FILES
    "${CMAKE_CURRENT_SOURCE_DIR}/s3.control"
    "${CMAKE_CURRENT_SOURCE_DIR}/dist/s3--0.1.0.sql"
    DESTINATION ${CMAKE_INSTALL_DATAROOTDIR}/postgresql/extension
)

install(FILES
    "${CMAKE_CURRENT_SOURCE_DIR}/s3.control"
    "${CMAKE_CURRENT_SOURCE_DIR}/dist/s3--0.1.0.sql"
    DESTINATION "${PG_SHAREDIR}/extension"
)
