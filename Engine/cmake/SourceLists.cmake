# ==============================================================================
# Source Lists for Engine
# ==============================================================================
# Explicit source file lists (replaces GLOB_RECURSE)
# This ensures CMake detects new files and rebuilds when source structure changes
# ==============================================================================

# --- CORE SOURCES (Math, Geometry, Hashing, ML) ---
# NO Database dependencies allowed here
set(ENGINE_CORE_SOURCES
    # Geometry
    ${CMAKE_CURRENT_SOURCE_DIR}/src/geometry/s3_bbox.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/geometry/s3_centroid.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/geometry/s3_distance.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/geometry/super_fibonacci.cpp
    
    # Hashing
    ${CMAKE_CURRENT_SOURCE_DIR}/src/hashing/blake3_pipeline.cpp
    
    # ML
    ${CMAKE_CURRENT_SOURCE_DIR}/src/ml/model_extraction.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/ml/s3_hnsw.cpp
)

# --- IO SOURCES (Database, Ingestion, Cognitive) ---
set(ENGINE_IO_SOURCES
    # Database
    ${CMAKE_CURRENT_SOURCE_DIR}/src/database/bulk_copy.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/database/postgres_connection.cpp
    
    # Ingestion
    ${CMAKE_CURRENT_SOURCE_DIR}/src/ingestion/model_ingester.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/ingestion/model_package_loader.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/ingestion/ngram_extractor.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/ingestion/safetensor_ingester.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/ingestion/safetensor_loader.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/ingestion/text_ingester.cpp
    
    # Query
    ${CMAKE_CURRENT_SOURCE_DIR}/src/query/ai_ops.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/query/semantic_query.cpp
    
    # Cognitive
    ${CMAKE_CURRENT_SOURCE_DIR}/src/cognitive/godel_engine.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/cognitive/ooda_loop.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/cognitive/walk_engine.cpp
    
    # Storage
    ${CMAKE_CURRENT_SOURCE_DIR}/src/storage/atom_lookup.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/storage/atom_store.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/storage/composition_store.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/storage/content_store.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/storage/physicality_store.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/storage/relation_evidence_store.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/storage/relation_store.cpp
    
    # Unicode Ingestor
    ${CMAKE_CURRENT_SOURCE_DIR}/src/unicode/ingestor/node_generator.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/unicode/ingestor/semantic_sequencer.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/unicode/ingestor/ucd_parser.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/src/unicode/ingestor/ucd_processor.cpp
    
    # Interop API
    ${CMAKE_CURRENT_SOURCE_DIR}/src/interop_api.cpp
)

# --- HEADERS ---
set(ENGINE_HEADERS
    # Cognitive
    ${CMAKE_CURRENT_SOURCE_DIR}/include/cognitive/godel_engine.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/cognitive/ooda_loop.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/cognitive/walk_engine.hpp
    
    # Database
    ${CMAKE_CURRENT_SOURCE_DIR}/include/database/bulk_copy.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/database/postgres_connection.hpp
    
    # Geometry
    ${CMAKE_CURRENT_SOURCE_DIR}/include/geometry/hopf_fibration.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/geometry/s3_bbox.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/geometry/s3_centroid.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/geometry/s3_distance.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/geometry/s3_vec.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/geometry/super_fibonacci.hpp
    
    # Hashing
    ${CMAKE_CURRENT_SOURCE_DIR}/include/hashing/blake3_pipeline.hpp
    
    # Ingestion
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ingestion/model_ingester.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ingestion/model_package_loader.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ingestion/ngram_extractor.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ingestion/safetensor_ingester.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ingestion/safetensor_loader.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ingestion/sequitur.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ingestion/text_ingester.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ingestion/universal_ingester.hpp
    
    # ML
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ml/embedding_projection.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ml/model_extraction.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ml/s3_hnsw.hpp
    
    # Query
    ${CMAKE_CURRENT_SOURCE_DIR}/include/query/ai_ops.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/query/semantic_query.hpp
    
    # Spatial
    ${CMAKE_CURRENT_SOURCE_DIR}/include/spatial/hilbert_curve_4d.hpp
    
    # Storage
    ${CMAKE_CURRENT_SOURCE_DIR}/include/storage/atom_lookup.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/storage/atom_store.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/storage/composition_store.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/storage/content_store.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/storage/physicality_store.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/storage/relation_evidence_store.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/storage/relation_store.hpp
    
    # Unicode
    ${CMAKE_CURRENT_SOURCE_DIR}/include/unicode/codepoint_projection.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/unicode/semantic_assignment.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/unicode/ingestor/node_generator.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/unicode/ingestor/semantic_sequencer.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/unicode/ingestor/ucd_models.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/unicode/ingestor/ucd_parser.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/unicode/ingestor/ucd_processor.hpp
    
    # Root headers
    ${CMAKE_CURRENT_SOURCE_DIR}/include/export.hpp
    ${CMAKE_CURRENT_SOURCE_DIR}/include/interop_api.h
)
