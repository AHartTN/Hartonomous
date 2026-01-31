cmake_minimum_required(VERSION 3.22)

# Seed Unicode tool
add_executable(seed_unicode seed_unicode.cpp)

target_include_directories(seed_unicode PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../include
    ${CMAKE_CURRENT_SOURCE_DIR}/../external/blake3/c
)

target_include_directories(seed_unicode SYSTEM PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../external/eigen
    ${CMAKE_CURRENT_SOURCE_DIR}/../external/spectra/include
)

# Link directly to avoid MKL leakage
find_package(PostgreSQL REQUIRED)
target_link_libraries(seed_unicode PRIVATE
    engine
    PostgreSQL::PostgreSQL
    BLAKE3::BLAKE3
)

# Install to build directory
install(TARGETS seed_unicode DESTINATION ${CMAKE_BINARY_DIR}/tools)

add_executable(ingest_text ingest_text.cpp)
target_include_directories(ingest_text PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../include)
target_link_libraries(ingest_text PRIVATE engine)
install(TARGETS ingest_text DESTINATION ${CMAKE_BINARY_DIR}/tools)
