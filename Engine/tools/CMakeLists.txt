# ==============================================================================
# Hartonomous Engine Tools
# ==============================================================================
# Command-line tools that link against Engine libraries.
# All tools link engine_io which transitively provides engine_core, PostgreSQL,
# PostGIS, BLAKE3, Eigen, etc.
# ==============================================================================

# jemalloc: 2-3x faster multi-threaded allocation than glibc malloc.
# Especially impactful for ingesters doing millions of small allocations.
find_library(JEMALLOC_LIB jemalloc)
if(JEMALLOC_LIB)
    message(STATUS "jemalloc found: ${JEMALLOC_LIB} (linked to all tools)")
endif()

# Helper: create tool target with engine_io + jemalloc
function(add_engine_tool name source)
    add_executable(${name} ${source})
    target_link_libraries(${name} PRIVATE engine_io)
    if(JEMALLOC_LIB)
        target_link_libraries(${name} PRIVATE ${JEMALLOC_LIB})
    endif()
endfunction()

add_engine_tool(seed_unicode seed_unicode.cpp)
add_engine_tool(ingest_text ingest_text.cpp)
add_engine_tool(ingest_model ingest_model.cpp)
add_engine_tool(ingest_wordnet_omw ingest_wordnet_omw.cpp)
add_engine_tool(ingest_tatoeba ingest_tatoeba.cpp)
add_engine_tool(ingest_ud ingest_ud.cpp)
add_engine_tool(ingest_wiktionary_xml ingest_wiktionary_xml.cpp)
add_engine_tool(walk_test walk_test.cpp)

# Install all tools
install(TARGETS seed_unicode ingest_text ingest_model ingest_wordnet_omw ingest_tatoeba ingest_ud ingest_wiktionary_xml walk_test
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)
