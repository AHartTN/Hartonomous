cmake_minimum_required(VERSION 3.15)
project(cortex_extension LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_STANDARD 11)

# Find PostgreSQL
find_program(PG_CONFIG pg_config)
if(NOT PG_CONFIG)
    message(FATAL_ERROR "pg_config not found. Please install PostgreSQL development files.")
endif()

# Get PostgreSQL configuration
execute_process(COMMAND ${PG_CONFIG} --includedir OUTPUT_VARIABLE PG_INCLUDE OUTPUT_STRIP_TRAILING_WHITESPACE)
execute_process(COMMAND ${PG_CONFIG} --includedir-server OUTPUT_VARIABLE PG_INCLUDE_SERVER OUTPUT_STRIP_TRAILING_WHITESPACE)
execute_process(COMMAND ${PG_CONFIG} --pkglibdir OUTPUT_VARIABLE PG_PKGLIBDIR OUTPUT_STRIP_TRAILING_WHITESPACE)
execute_process(COMMAND ${PG_CONFIG} --sharedir OUTPUT_VARIABLE PG_SHAREDIR OUTPUT_STRIP_TRAILING_WHITESPACE)

# Windows-specific: Add port includes for compatibility headers (netinet, dirent, etc.)
if(WIN32)
    set(PG_PORT_INCLUDE "${PG_INCLUDE_SERVER}/port/win32")
    set(PG_PORT_MSVC_INCLUDE "${PG_INCLUDE_SERVER}/port/win32_msvc")
endif()

message(STATUS "PostgreSQL include: ${PG_INCLUDE}")
message(STATUS "PostgreSQL include server: ${PG_INCLUDE_SERVER}")
message(STATUS "PostgreSQL lib dir: ${PG_PKGLIBDIR}")
message(STATUS "PostgreSQL share dir: ${PG_SHAREDIR}")

# Try to find Eigen3
find_package(Eigen3 3.3 QUIET NO_MODULE)
if(NOT Eigen3_FOUND)
    message(WARNING "Eigen3 not found via find_package, trying manual paths")
    set(EIGEN3_INCLUDE_DIRS 
        "D:/eigen3/eigen-3.4.0" 
        "C:/eigen3/eigen-3.4.0"
        "D:/eigen3/include/eigen3"
        "C:/Program Files/eigen3/include/eigen3" 
        "/usr/include/eigen3"
    )
endif()

# Add source files
add_library(cortex SHARED
    cortex.c
    lmds_projector.cpp
)

# Include directories
target_include_directories(cortex PRIVATE
    ${PG_INCLUDE}
    ${PG_INCLUDE_SERVER}
    ${PG_PORT_INCLUDE}
    ${PG_PORT_MSVC_INCLUDE}
    ${EIGEN3_INCLUDE_DIRS}
)

# Link PostgreSQL libraries
execute_process(COMMAND ${PG_CONFIG} --libdir OUTPUT_VARIABLE PG_LIBDIR OUTPUT_STRIP_TRAILING_WHITESPACE)
target_link_libraries(cortex PRIVATE "${PG_LIBDIR}/postgres.lib")

# Compile definitions for PostgreSQL
if(WIN32)
    target_compile_definitions(cortex PRIVATE
        WIN32
        _WINDOWS
        __WINDOWS__
        __WIN32__
        _CRT_SECURE_NO_WARNINGS
    )
endif()

# Set output properties
set_target_properties(cortex PROPERTIES
    PREFIX ""
    OUTPUT_NAME "cortex"
    SUFFIX ".dll"
)

# Installation
install(TARGETS cortex DESTINATION ${PG_PKGLIBDIR})
install(FILES cortex.control DESTINATION "${PG_SHAREDIR}/extension")
install(FILES cortex--1.0.sql DESTINATION "${PG_SHAREDIR}/extension")

# Print build configuration
message(STATUS "Cortex PostgreSQL extension configured for Windows build")
message(STATUS "Extension will install to: ${PG_PKGLIBDIR}")
