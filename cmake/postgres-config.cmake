# cmake/postgres-config.cmake

if(WIN32)
    # 1. On Windows, we prefer to find PostgreSQL via find_package which is more robust
    find_package(PostgreSQL QUIET)
    
    if(PostgreSQL_FOUND)
        set(PG_CLIENT_INCLUDE_DIR ${PostgreSQL_INCLUDE_DIRS})
        set(PostgreSQL_LIBRARY ${PostgreSQL_LIBRARIES})
        set(PG_SERVER_INCLUDE_DIR "${PostgreSQL_INCLUDE_DIRS}/server")
    else()
        # Fallback to manual search if find_package fails
        set(PG_SEARCH_PATHS 
            "C:/Program Files/PostgreSQL/17"
            "C:/Program Files/PostgreSQL/16"
            "C:/Program Files/PostgreSQL/15"
        )
        find_path(PG_CLIENT_INCLUDE_DIR NAMES libpq-fe.h PATHS ${PG_SEARCH_PATHS} PATH_SUFFIXES include)
        find_path(PG_SERVER_INCLUDE_DIR NAMES postgres.h PATHS ${PG_SEARCH_PATHS} PATH_SUFFIXES include/server)
        find_library(PostgreSQL_LIBRARY NAMES libpq pq PATHS ${PG_SEARCH_PATHS} PATH_SUFFIXES lib)
    endif()
else()
    # Linux/UNIX: Use pg_config as it's the standard
    find_program(PG_CONFIG NAMES pg_config REQUIRED)

    execute_process(COMMAND ${PG_CONFIG} --includedir OUTPUT_VARIABLE PG_CLIENT_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${PG_CONFIG} --includedir-server OUTPUT_VARIABLE PG_SERVER_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${PG_CONFIG} --libdir OUTPUT_VARIABLE PG_LIBDIR OUTPUT_STRIP_TRAILING_WHITESPACE)

    find_library(PostgreSQL_LIBRARY 
        NAMES pq libpq 
        PATHS "${PG_LIBDIR}" 
        NO_DEFAULT_PATH
    )
endif()

# 2. Define Targets
if(PostgreSQL_LIBRARY AND PG_CLIENT_INCLUDE_DIR)
    # Target A: Client (Engine uses this)
    if(NOT TARGET PostgreSQL::LibPQ)
        add_library(PostgreSQL::LibPQ UNKNOWN IMPORTED)
        set_target_properties(PostgreSQL::LibPQ PROPERTIES
            IMPORTED_LOCATION "${PostgreSQL_LIBRARY}"
            INTERFACE_INCLUDE_DIRECTORIES "${PG_CLIENT_INCLUDE_DIR}"
        )
        message(STATUS "PostgreSQL::LibPQ target created.")
    endif()

    # Target B: Server (Extensions use this)
    if(NOT TARGET PostgreSQL::Server AND PG_SERVER_INCLUDE_DIR)
        add_library(PostgreSQL::Server INTERFACE IMPORTED)
        set_target_properties(PostgreSQL::Server PROPERTIES
            INTERFACE_INCLUDE_DIRECTORIES "${PG_SERVER_INCLUDE_DIR}"
        )
        message(STATUS "PostgreSQL::Server target created.")
    endif()

    message(STATUS "PostgreSQL Configured.")
else()
    message(FATAL_ERROR "Could not find PostgreSQL (libpq). Please ensure PostgreSQL is installed and in the PATH.")
endif()
