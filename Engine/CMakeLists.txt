cmake_minimum_required(VERSION 3.22)

project(HartonomousEngine LANGUAGES C CXX)

# ==============================================================================
#  INCLUDE ORGANIZED CMAKE MODULES
# ==============================================================================
list(APPEND CMAKE_MODULE_PATH "${CMAKE_SOURCE_DIR}/cmake/utils")
include(CompilerFlags)
include(cmake/Dependencies.cmake)
include(cmake/SourceLists.cmake)

# ==============================================================================
#  OBJECT LIBRARIES (Compiled once, used multiple times)
# ==============================================================================
# Compile sources into object files that can be reused
add_library(engine_core_objs OBJECT ${ENGINE_CORE_SOURCES} ${ENGINE_HEADERS})
add_library(engine_io_objs OBJECT ${ENGINE_IO_SOURCES} ${ENGINE_HEADERS})

# Enable PIC for shared library linking
enable_pic(engine_core_objs)
enable_pic(engine_io_objs)

# Set compile definitions
target_compile_definitions(engine_core_objs PRIVATE HARTONOMOUS_EXPORT)
target_compile_definitions(engine_io_objs PRIVATE HARTONOMOUS_EXPORT)

# Configure includes and dependencies for object libraries
target_include_directories(engine_core_objs
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        ${CMAKE_CURRENT_SOURCE_DIR}/external/hilbert
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_include_directories(engine_io_objs
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

# Object libraries need to know about dependencies for includes
target_link_libraries(engine_core_objs
    PUBLIC
        BLAKE3::BLAKE3
        Eigen3::Eigen
        HNSW::HNSW
        OpenMP::OpenMP_CXX
    PRIVATE
        Spectra::Spectra
        MKL::MKL
)

target_link_libraries(engine_io_objs
    PUBLIC
        engine_core_objs  # Need core headers/includes
        PostGIS::PostGIS
        PostgreSQL::LibPQ
        tree-sitter::tree-sitter
        nlohmann_json::nlohmann_json
        BLAKE3::BLAKE3      # IO code includes blake3 headers
        Eigen3::Eigen
        HNSW::HNSW
        OpenMP::OpenMP_CXX
)

# ==============================================================================
#  TARGET: ENGINE CORE (Geometry/Math)
# ==============================================================================
# Use pre-compiled object files - NO RECOMPILATION!
add_library(engine_core SHARED $<TARGET_OBJECTS:engine_core_objs>)
add_library(Hartonomous::Core ALIAS engine_core)

set_target_properties(engine_core PROPERTIES
    OUTPUT_NAME "engine_core"
)

# Inherit includes and dependencies from object library
target_include_directories(engine_core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        ${CMAKE_CURRENT_SOURCE_DIR}/external/hilbert
)

target_link_libraries(engine_core
    PUBLIC
        BLAKE3::BLAKE3
        Eigen3::Eigen
        HNSW::HNSW
        OpenMP::OpenMP_CXX
    PRIVATE
        Spectra::Spectra
        MKL::MKL
)

# Apply optimized compiler flags
apply_hartonomous_compiler_flags(engine_core)

# ==============================================================================
#  TARGET: ENGINE IO (Database/Ingestion)
# ==============================================================================
# Use pre-compiled object files - NO RECOMPILATION!
add_library(engine_io SHARED $<TARGET_OBJECTS:engine_io_objs>)
add_library(Hartonomous::IO ALIAS engine_io)

set_target_properties(engine_io PROPERTIES
    OUTPUT_NAME "engine_io"
)

target_include_directories(engine_io
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(engine_io
    PUBLIC
        engine_core
        PostGIS::PostGIS
        PostgreSQL::LibPQ
        tree-sitter::tree-sitter
        nlohmann_json::nlohmann_json
)

# Apply optimized compiler flags
apply_hartonomous_compiler_flags(engine_io)

# ==============================================================================
#  TARGET: UNIFIED ENGINE (for .NET interop)
# ==============================================================================
# Combine BOTH object libraries into one unified .so for .NET P/Invoke
# This is what the .NET build expects - all symbols in one library
# Uses pre-compiled objects - NO RECOMPILATION!
add_library(engine SHARED
    $<TARGET_OBJECTS:engine_core_objs>
    $<TARGET_OBJECTS:engine_io_objs>
)
add_library(Hartonomous::Engine ALIAS engine)

set_target_properties(engine PROPERTIES
    OUTPUT_NAME "engine"
)

target_include_directories(engine
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        ${CMAKE_CURRENT_SOURCE_DIR}/external/hilbert
)

# Unified library needs ALL dependencies
target_link_libraries(engine
    PUBLIC
        BLAKE3::BLAKE3
        Eigen3::Eigen
        HNSW::HNSW
        OpenMP::OpenMP_CXX
        PostGIS::PostGIS
        PostgreSQL::LibPQ
        tree-sitter::tree-sitter
        nlohmann_json::nlohmann_json
    PRIVATE
        Spectra::Spectra
        MKL::MKL
)

# Apply optimized compiler flags
apply_hartonomous_compiler_flags(engine)

# ==============================================================================
#  TOOLS & TESTS
# ==============================================================================
add_subdirectory(tools)
enable_testing()
add_subdirectory(tests)
# ==============================================================================
#  INSTALLATION
# ==============================================================================
include(GNUInstallDirs)

# Install all three libraries
install(TARGETS engine_core engine_io engine
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

# Install headers
install(DIRECTORY include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}/hartonomous)