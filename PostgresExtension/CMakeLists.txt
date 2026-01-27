cmake_minimum_required(VERSION 3.25)

project(HartonomousPostgresExt LANGUAGES C CXX)

# ==============================================================================
#  FIND POSTGRESQL
# ==============================================================================
find_program(PG_CONFIG pg_config)

if(NOT PG_CONFIG)
    message(FATAL_ERROR "pg_config not found! Please ensure PostgreSQL is installed and in your PATH.")
endif()

execute_process(
    COMMAND ${PG_CONFIG} --includedir-server
    OUTPUT_VARIABLE PG_SERVER_INCLUDE_DIR
    OUTPUT_STRIP_TRAILING_WHITESPACE
)

message(STATUS "Found PostgreSQL Server Includes: ${PG_SERVER_INCLUDE_DIR}")

# ==============================================================================
#  EXTENSION TARGET
# ==============================================================================
set(EXTENSION_NAME "hartonomous")

file(GLOB_RECURSE EXT_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.c"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp"
)

add_library(${EXTENSION_NAME} MODULE ${EXT_SOURCES})

# ==============================================================================
#  INCLUDES & LINKING
# ==============================================================================
target_include_directories(${EXTENSION_NAME}
    PRIVATE
        ${PG_SERVER_INCLUDE_DIR}
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_link_libraries(${EXTENSION_NAME}
    PRIVATE
        Hartonomous::Engine
)

# ==============================================================================
#  PROPERTIES
# ==============================================================================
if(MSVC)
    target_compile_definitions(${EXTENSION_NAME} PRIVATE "DLLEXPORT=__declspec(dllexport)")
    
    # CRITICAL FIX: Explicitly ignore Static CRT to prevent LNK4098 warning
    # This ensures no static libraries (LIBCMT) accidentally get linked into our DLL.
    target_link_options(${EXTENSION_NAME} PRIVATE "/NODEFAULTLIB:LIBCMT")
endif()

set_target_properties(${EXTENSION_NAME} PROPERTIES PREFIX "")