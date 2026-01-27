set(BLAKE3_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/blake3" CACHE PATH "BLAKE3 root")

add_library(BLAKE3 STATIC
    "${BLAKE3_ROOT}/c/blake3.c"
    "${BLAKE3_ROOT}/c/blake3_dispatch.c"
    "${BLAKE3_ROOT}/c/blake3_portable.c"
    "${BLAKE3_ROOT}/c/blake3_sse2.c"
    "${BLAKE3_ROOT}/c/blake3_sse41.c"
    "${BLAKE3_ROOT}/c/blake3_avx2.c"
    "${BLAKE3_ROOT}/c/blake3_avx512.c"
)

target_include_directories(BLAKE3 PUBLIC
    "${BLAKE3_ROOT}/c"
)
