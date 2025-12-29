cmake_minimum_required(VERSION 3.25)

project(Hartonomous.Native
    VERSION 0.1.0
    DESCRIPTION "Universal semantic substrate - atoms, compositions, projections"
    LANGUAGES CXX
)

# =============================================================================
# Project Options
# =============================================================================
option(BUILD_TESTING "Build tests" ON)
option(BUILD_CLI "Build CLI tools (requires PostgreSQL)" OFF)

# =============================================================================
# Global Settings
# =============================================================================
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# All outputs go to same directory - no DLL hunting
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)

# MinGW/Clang needs pthread for std::mutex
if(MINGW)
    find_package(Threads REQUIRED)
endif()

# =============================================================================
# Dependencies
# =============================================================================
if(BUILD_TESTING)
    find_package(Catch2 3 CONFIG REQUIRED)
endif()

# =============================================================================
# PostgreSQL (optional - for database operations)
# =============================================================================
# Find postgres/psql in PATH, derive root from executable location
find_program(PG_EXECUTABLE NAMES postgres psql)
if(PG_EXECUTABLE)
    get_filename_component(PG_BIN_DIR "${PG_EXECUTABLE}" DIRECTORY)
    get_filename_component(PG_ROOT "${PG_BIN_DIR}" DIRECTORY)
endif()

# Allow env var override
if(DEFINED ENV{POSTGRESQL_ROOT})
    set(PG_ROOT "$ENV{POSTGRESQL_ROOT}")
endif()

set(HARTONOMOUS_HAS_DATABASE OFF)
if(PG_ROOT AND EXISTS "${PG_ROOT}/include/libpq-fe.h" AND EXISTS "${PG_ROOT}/bin/libpq.dll")
    add_library(PostgreSQL::PostgreSQL IMPORTED INTERFACE)
    set_target_properties(PostgreSQL::PostgreSQL PROPERTIES
        INTERFACE_INCLUDE_DIRECTORIES "${PG_ROOT}/include"
        INTERFACE_LINK_LIBRARIES "${PG_ROOT}/bin/libpq.dll"
    )
    set(HARTONOMOUS_HAS_DATABASE ON)
    message(STATUS "PostgreSQL: ${PG_ROOT}")
    message(STATUS "Database tests enabled")
else()
    message(STATUS "PostgreSQL not found - add postgres/psql to PATH or set POSTGRESQL_ROOT")
endif()

# =============================================================================
# Library: hartonomous-core (header-only)
# =============================================================================
add_library(hartonomous-core INTERFACE)
target_include_directories(hartonomous-core INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/src>
    $<INSTALL_INTERFACE:include>
)
target_compile_features(hartonomous-core INTERFACE cxx_std_20)

# =============================================================================
# Library: Hartonomous.Native (shared library for P/Invoke)
# =============================================================================
add_library(Hartonomous.Native SHARED
    src/geometry/tesseract_surface.cpp
    src/unicode/codepoint_mapper.cpp
    src/unicode/unicode_blocks.cpp
    src/api/exports.cpp
)

# Link PostgreSQL if available for database operations
if(TARGET PostgreSQL::PostgreSQL)
    target_link_libraries(Hartonomous.Native PRIVATE hartonomous-core PostgreSQL::PostgreSQL $<$<BOOL:${MINGW}>:Threads::Threads>)
    target_compile_definitions(Hartonomous.Native PRIVATE HARTONOMOUS_HAS_DATABASE)
else()
    target_link_libraries(Hartonomous.Native PRIVATE hartonomous-core $<$<BOOL:${MINGW}>:Threads::Threads>)
endif()

target_include_directories(Hartonomous.Native PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/src>
    $<INSTALL_INTERFACE:include>
)

if(WIN32)
    target_compile_definitions(Hartonomous.Native PRIVATE HARTONOMOUS_EXPORTS)
endif()

# Copy MinGW runtime DLLs to bin directory for portability
if(MINGW)
    get_filename_component(MINGW_BIN_DIR ${CMAKE_CXX_COMPILER} DIRECTORY)
    set(MINGW_DLLS
        "${MINGW_BIN_DIR}/libstdc++-6.dll"
        "${MINGW_BIN_DIR}/libgcc_s_seh-1.dll"
        "${MINGW_BIN_DIR}/libwinpthread-1.dll"
    )
    foreach(DLL ${MINGW_DLLS})
        if(EXISTS "${DLL}")
            file(COPY "${DLL}" DESTINATION "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}")
        endif()
    endforeach()
endif()

# =============================================================================
# Standalone CLI: hartonomous-ingest (no external dependencies)
# =============================================================================
add_executable(hartonomous-ingest
    src/cli/ingest.cpp
)
target_link_libraries(hartonomous-ingest PRIVATE hartonomous-core Threads::Threads)
target_include_directories(hartonomous-ingest PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)
target_compile_definitions(hartonomous-ingest PRIVATE
    TEST_DATA_DIR="${CMAKE_CURRENT_SOURCE_DIR}/../test-data"
)

# =============================================================================
# Database CLI executables (requires PostgreSQL)
# =============================================================================
if(HARTONOMOUS_HAS_DATABASE)
    add_executable(hartonomous-seed src/cli/seed.cpp)
    target_link_libraries(hartonomous-seed PRIVATE hartonomous-core PostgreSQL::PostgreSQL Threads::Threads)
    target_include_directories(hartonomous-seed PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)

    # Benchmark: bench-moby (requires PostgreSQL for full database round-trip)
    add_executable(bench-moby tests/bench_moby.cpp)
    target_link_libraries(bench-moby PRIVATE hartonomous-core PostgreSQL::PostgreSQL Threads::Threads)
    target_include_directories(bench-moby PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)
    target_compile_definitions(bench-moby PRIVATE
        TEST_DATA_DIR="${CMAKE_CURRENT_SOURCE_DIR}/../test-data"
    )

    add_custom_command(TARGET hartonomous-seed POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${PG_ROOT}/bin/libpq.dll" "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}"
    )

    # CLI: hartonomous-ingest-all (universal content ingestion)
    add_executable(hartonomous-ingest-all src/cli/ingest_all.cpp)
    target_link_libraries(hartonomous-ingest-all PRIVATE hartonomous-core PostgreSQL::PostgreSQL Threads::Threads)
    target_include_directories(hartonomous-ingest-all PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)

    # CLI: hartonomous-model-ingest (AI model ingestion)
    add_executable(hartonomous-model-ingest src/cli/ingest_model.cpp)
    target_link_libraries(hartonomous-model-ingest PRIVATE hartonomous-core PostgreSQL::PostgreSQL Threads::Threads)
    target_include_directories(hartonomous-model-ingest PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)
endif()

# =============================================================================
# Tests
# =============================================================================
if(BUILD_TESTING)
    include(CTest)

    # Find threads package for std::mutex support
    find_package(Threads REQUIRED)

    # Single unified test executable
    add_executable(hartonomous-tests
        tests/test_main.cpp
        tests/test_invariants.cpp
        tests/test_encoding.cpp
        tests/test_spatial.cpp
        tests/test_vision.cpp
    )

    # Add database tests if PostgreSQL available
    if(HARTONOMOUS_HAS_DATABASE)
        target_sources(hartonomous-tests PRIVATE
            tests/test_database.cpp
            tests/test_query.cpp
            tests/test_safetensor.cpp
            tests/test_mlops.cpp
            tests/test_mlops_queries.cpp
            tests/test_mlops_proof.cpp
            tests/test_moby_dick.cpp
            tests/test_cpe_debug.cpp
            tests/test_connection_pool.cpp
            tests/test_bulk_store.cpp
            tests/test_semantic_linker.cpp
        )
        target_link_libraries(hartonomous-tests PRIVATE
            hartonomous-core
            Catch2::Catch2WithMain
            PostgreSQL::PostgreSQL
            Threads::Threads
        )
    else()
        target_link_libraries(hartonomous-tests PRIVATE
            hartonomous-core
            Catch2::Catch2WithMain
            Threads::Threads
        )
    endif()

    target_include_directories(hartonomous-tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
    )

    target_compile_definitions(hartonomous-tests PRIVATE
        TEST_DATA_DIR="${CMAKE_CURRENT_SOURCE_DIR}/../test-data"
    )

    add_test(NAME hartonomous-tests COMMAND hartonomous-tests)
endif()

# =============================================================================
# Install
# =============================================================================
include(GNUInstallDirs)

install(TARGETS hartonomous-core Hartonomous.Native
    EXPORT HartonomousTargets
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    INCLUDES DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}
)

# Install CLI tools
install(TARGETS hartonomous-ingest
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

if(TARGET hartonomous-seed)
    install(TARGETS hartonomous-seed
        RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    )
endif()

# Install all public headers
install(DIRECTORY src/atoms src/geometry src/hilbert src/unicode src/types src/api
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}/hartonomous
    FILES_MATCHING PATTERN "*.hpp" PATTERN "*.h"
)
