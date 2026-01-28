/**
 * @file hartonomous_functions.hpp
 * @brief PostgreSQL function declarations for Hartonomous extension
 */

#pragma once

extern "C" {
#include <postgres.h>
#include <fmgr.h>
}

// Extension initialization
#ifdef PG_MODULE_MAGIC
PG_MODULE_MAGIC;
#endif

// Function declarations (extern "C" for PostgreSQL)
extern "C" {

// Version info
PG_FUNCTION_INFO_V1(hartonomous_version);
Datum hartonomous_version(PG_FUNCTION_ARGS);

// BLAKE3 hashing
PG_FUNCTION_INFO_V1(blake3_hash);
Datum blake3_hash(PG_FUNCTION_ARGS);

PG_FUNCTION_INFO_V1(blake3_hash_codepoint);
Datum blake3_hash_codepoint(PG_FUNCTION_ARGS);

// Codepoint projection
PG_FUNCTION_INFO_V1(codepoint_to_s3);
Datum codepoint_to_s3(PG_FUNCTION_ARGS);

PG_FUNCTION_INFO_V1(codepoint_to_hilbert);
Datum codepoint_to_hilbert(PG_FUNCTION_ARGS);

// Centroid computation
PG_FUNCTION_INFO_V1(compute_centroid);
Datum compute_centroid(PG_FUNCTION_ARGS);

// Text ingestion (returns stats)
PG_FUNCTION_INFO_V1(ingest_text);
Datum ingest_text(PG_FUNCTION_ARGS);

// Semantic query
PG_FUNCTION_INFO_V1(semantic_search);
Datum semantic_search(PG_FUNCTION_ARGS);

} // extern "C"
