set(HNSW_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/hnswlib" CACHE PATH "HNSWLib root")

add_library(HNSW INTERFACE)

target_include_directories(HNSW INTERFACE
    "${HNSW_ROOT}"
)

target_compile_options(HNSW INTERFACE
    $<$<CXX_COMPILER_ID:MSVC>:/arch:AVX2>
    $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-mavx2>
)
