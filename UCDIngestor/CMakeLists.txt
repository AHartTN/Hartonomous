cmake_minimum_required(VERSION 3.10)
project(UCDIngestor CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Try finding libpqxx via PkgConfig first
find_package(PkgConfig QUIET)
if(PKG_CONFIG_FOUND)
    pkg_check_modules(LIBPQXX QUIET libpqxx)
endif()

# Fallback: Manual search if PkgConfig failed
if(NOT LIBPQXX_FOUND)
    find_path(LIBPQXX_INCLUDE_DIRS NAMES pqxx/pqxx HINTS /usr/include /usr/local/include)
    find_library(LIBPQXX_LIBRARIES NAMES pqxx HINTS /usr/lib /usr/lib/x86_64-linux-gnu /usr/local/lib)

    if(LIBPQXX_INCLUDE_DIRS AND LIBPQXX_LIBRARIES)
        set(LIBPQXX_FOUND TRUE)
        message(STATUS "Found libpqxx manually: ${LIBPQXX_LIBRARIES}")
    endif()
endif()

if(NOT LIBPQXX_FOUND)
    message(FATAL_ERROR "Could not find libpqxx. Please install libpqxx-dev.")
endif()

# Include directories
include_directories(
    include
    src
    ${LIBPQXX_INCLUDE_DIRS}
    ../Engine/external/json/include
)

# Source files (Restoring all files)
set(SOURCE_FILES
    src/main.cpp
    src/PgConnection.cpp
    src/UcdXmlReader.cpp
    src/UcdIngestor.cpp
    src/parsers/AllKeysParser.cpp
    src/parsers/ConfusablesParser.cpp
)

# Add the executable
add_executable(ucd_ingestor ${SOURCE_FILES})

# Link libraries
target_link_libraries(ucd_ingestor ${LIBPQXX_LDFLAGS} ${LIBPQXX_LIBRARIES} pq)

# Set RPATH
if(UNIX)
    set_target_properties(ucd_ingestor PROPERTIES
        BUILD_RPATH "${LIBPQXX_LIBRARY_DIRS}"
        INSTALL_RPATH "${LIBPQXX_LIBRARY_DIRS}"
    )
endif()