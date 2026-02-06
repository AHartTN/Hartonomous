# ==============================================================================
# Hartonomous Engine Tools
# ==============================================================================
# Command-line tools that link against Engine libraries.
# All tools link engine_io which transitively provides engine_core, PostgreSQL,
# PostGIS, BLAKE3, Eigen, etc.
# ==============================================================================

# --- Seed Unicode: Populate atom table with all 1.114M Unicode codepoints ---
add_executable(seed_unicode seed_unicode.cpp)
target_link_libraries(seed_unicode PRIVATE engine_io)

# --- Ingest Text: Text → Atoms → Compositions → Relations ---
add_executable(ingest_text ingest_text.cpp)
target_link_libraries(ingest_text PRIVATE engine_io)

# --- Ingest Model: Extract relationships from AI model weights ---
add_executable(ingest_model ingest_model.cpp)
target_link_libraries(ingest_model PRIVATE engine_io)

# --- Walk Test: Test graph navigation ---
add_executable(walk_test walk_test.cpp)
target_link_libraries(walk_test PRIVATE engine_io)

# Install all tools
install(TARGETS seed_unicode ingest_text ingest_model walk_test
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)
