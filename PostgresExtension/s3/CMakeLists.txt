cmake_minimum_required(VERSION 3.22)

project(S3PostgresExt LANGUAGES CXX)

if(WIN32)
    find_package(PostgreSQL REQUIRED)
    set(PG_SERVER_INCLUDE_DIR "${PostgreSQL_INCLUDE_DIRS}/server")
    set(PG_PKGLIBDIR "${PostgreSQL_INCLUDE_DIRS}/../lib")
    set(PG_SHAREDIR "${PostgreSQL_INCLUDE_DIRS}/../share")
    set(MODULE_SUFFIX ".dll")
else()
    find_program(PG_CONFIG NAMES pg_config REQUIRED)
    execute_process(COMMAND ${PG_CONFIG} --includedir-server OUTPUT_VARIABLE PG_SERVER_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${PG_CONFIG} --pkglibdir         OUTPUT_VARIABLE PG_PKGLIBDIR        OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${PG_CONFIG} --sharedir          OUTPUT_VARIABLE PG_SHAREDIR         OUTPUT_STRIP_TRAILING_WHITESPACE)
    set(MODULE_SUFFIX ".so")
endif()

set(EXTENSION_NAME "s3")

set(EXT_SOURCES
    "src/pg/s3_pg_shim.cpp"
    "src/pg/s3_pg_gist.cpp"
    "src/pg/s3_pg_geom.cpp"
)

add_library(${EXTENSION_NAME} MODULE ${EXT_SOURCES})

target_include_directories(${EXTENSION_NAME}
    PRIVATE
        ${PG_SERVER_INCLUDE_DIR}
        ${CMAKE_SOURCE_DIR}/Engine/include
)

if(WIN32)
    find_library(POSTGRES_LIB NAMES postgres PATHS "${PostgreSQL_INCLUDE_DIRS}/../lib")
    target_link_libraries(${EXTENSION_NAME} PRIVATE ${POSTGRES_LIB})
endif()

# Link against the Engine core and PostGIS
target_link_libraries(${EXTENSION_NAME}
    PRIVATE
        engine_core
        PostGIS::PostGIS
)

set_target_properties(${EXTENSION_NAME} PROPERTIES
    PREFIX ""
    SUFFIX "${MODULE_SUFFIX}"
)

install(TARGETS ${EXTENSION_NAME} LIBRARY DESTINATION "${PG_PKGLIBDIR}")

install(FILES
    "s3.control"
    "dist/s3--0.1.0.sql"
    DESTINATION "${PG_SHAREDIR}/extension"
)
