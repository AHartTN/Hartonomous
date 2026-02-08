cmake_minimum_required(VERSION 3.22)

project(HartonomousPostgresExt LANGUAGES C)

# ==============================================================================
#  1. AUTO-DETECT POSTGRESQL
# ==============================================================================

if(WIN32)
    # Windows: Manual search or find_package
    find_package(PostgreSQL REQUIRED)
    set(PG_SERVER_INCLUDE_DIR "${PostgreSQL_INCLUDE_DIRS}/server")
    set(PG_PKGLIBDIR "${PostgreSQL_INCLUDE_DIRS}/../lib")
    set(PG_SHAREDIR "${PostgreSQL_INCLUDE_DIRS}/../share")
    set(MODULE_SUFFIX ".dll")
else()
    # Linux/UNIX: pg_config
    find_program(PG_CONFIG NAMES pg_config REQUIRED)
    execute_process(COMMAND ${PG_CONFIG} --includedir-server OUTPUT_VARIABLE PG_SERVER_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${PG_CONFIG} --pkglibdir         OUTPUT_VARIABLE PG_PKGLIBDIR        OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${PG_CONFIG} --sharedir          OUTPUT_VARIABLE PG_SHAREDIR         OUTPUT_STRIP_TRAILING_WHITESPACE)
    set(MODULE_SUFFIX ".so")
endif()

# ==============================================================================
#  2. TARGET DEFINITION
# ==============================================================================

set(EXTENSION_NAME "hartonomous")

# Hartonomous extension is a pure C shim
set(EXT_SOURCES
    "src/hartonomous_shim.c"
    "src/uint128_ops.c"
    "src/uint64_ops.c"
)

add_library(${EXTENSION_NAME} MODULE ${EXT_SOURCES})

# ==============================================================================
#  3. INCLUDES & DEPENDENCIES
# ==============================================================================

target_include_directories(${EXTENSION_NAME}
    PRIVATE
        ${PG_SERVER_INCLUDE_DIR}
        ${CMAKE_SOURCE_DIR}/Engine/include
)

# On Windows, we must link against postgres.lib to resolve server symbols
if(WIN32)
    find_library(POSTGRES_LIB NAMES postgres PATHS "${PostgreSQL_INCLUDE_DIRS}/../lib")
    target_link_libraries(${EXTENSION_NAME} PRIVATE ${POSTGRES_LIB})
endif()

# Link against the Engine core
target_link_libraries(${EXTENSION_NAME}
    PRIVATE
        engine_io
)

# ==============================================================================
#  4. CONFIGURATION
# ==============================================================================

set_target_properties(${EXTENSION_NAME} PROPERTIES
    PREFIX ""
    SUFFIX "${MODULE_SUFFIX}"
)

# ==============================================================================
#  5. INSTALLATION
# ==============================================================================

install(TARGETS ${EXTENSION_NAME} LIBRARY DESTINATION "${PG_PKGLIBDIR}")

install(FILES
    "hartonomous.control"
    "dist/hartonomous--0.1.0.sql"
    DESTINATION "${PG_SHAREDIR}/extension"
)
