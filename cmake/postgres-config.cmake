# cmake/postgres-config.cmake

# 1. Find pg_config
find_program(PG_CONFIG NAMES pg_config REQUIRED)

# 2. Get Paths
#    --includedir        -> /usr/include/postgresql (Client headers: libpq-fe.h)
#    --includedir-server -> /usr/include/postgresql/18/server (Server headers: postgres.h)
#    --libdir            -> /usr/lib/x86_64-linux-gnu (Libraries: libpq.so)
execute_process(COMMAND ${PG_CONFIG} --includedir OUTPUT_VARIABLE PG_CLIENT_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
execute_process(COMMAND ${PG_CONFIG} --includedir-server OUTPUT_VARIABLE PG_SERVER_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
execute_process(COMMAND ${PG_CONFIG} --libdir OUTPUT_VARIABLE PG_LIBDIR OUTPUT_STRIP_TRAILING_WHITESPACE)

# 3. Find the Client Library (libpq)
find_library(PostgreSQL_LIBRARY 
    NAMES pq libpq 
    PATHS "${PG_LIBDIR}" 
    NO_DEFAULT_PATH
)

# 4. Define Targets
if(PostgreSQL_LIBRARY)
    # Target A: Client (Engine uses this)
    if(NOT TARGET PostgreSQL::LibPQ)
        add_library(PostgreSQL::LibPQ UNKNOWN IMPORTED)
        set_target_properties(PostgreSQL::LibPQ PROPERTIES
            IMPORTED_LOCATION "${PostgreSQL_LIBRARY}"
            INTERFACE_INCLUDE_DIRECTORIES "${PG_CLIENT_INCLUDE_DIR}"
        )
    endif()

    # Target B: Server (Extensions use this)
    if(NOT TARGET PostgreSQL::Server)
        add_library(PostgreSQL::Server INTERFACE IMPORTED)
        set_target_properties(PostgreSQL::Server PROPERTIES
            INTERFACE_INCLUDE_DIRECTORIES "${PG_SERVER_INCLUDE_DIR}"
        )
    endif()

    message(STATUS "PostgreSQL Configured: Client & Server targets ready.")
else()
    message(FATAL_ERROR "Found pg_config at ${PG_CONFIG} but could not find libpq in ${PG_LIBDIR}")
endif()