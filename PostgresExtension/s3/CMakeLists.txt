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

# Handle modular SQL flattening
set(SQL_SRC "${CMAKE_CURRENT_SOURCE_DIR}/sql/s3--0.1.0.sql")
set(SQL_DIST "${CMAKE_CURRENT_SOURCE_DIR}/dist/s3--0.1.0.sql")
set(FLATTEN_SCRIPT "${CMAKE_SOURCE_DIR}/scripts/linux/XX-build_extension_sql.sh")

add_custom_command(
    OUTPUT ${SQL_DIST}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${CMAKE_CURRENT_SOURCE_DIR}/dist
    COMMAND ${FLATTEN_SCRIPT} ${SQL_SRC} ${SQL_DIST}
    DEPENDS ${SQL_SRC} ${FLATTEN_SCRIPT}
    COMMENT "Flattening modular SQL for s3 extension"
)

add_custom_target(s3_sql_dist ALL DEPENDS ${SQL_DIST})
add_dependencies(${EXTENSION_NAME} s3_sql_dist)

# ==============================================================================
#  5. INSTALLATION
# ==============================================================================

install(TARGETS ${EXTENSION_NAME}
    LIBRARY DESTINATION "${PG_PKGLIBDIR}"
)

install(FILES
    "s3.control"
    "${SQL_DIST}"
    DESTINATION "${PG_SHAREDIR}/extension"
)
