/**
 * @file hartonomous_functions.hpp
 * @brief PostgreSQL function declarations for Hartonomous extension
 */

#pragma once

// Windows compatibility: Include winsock2.h BEFORE postgres.h
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#endif

extern "C" {
#include <postgres.h>
#include <fmgr.h>
}

// Function declarations (extern "C" for PostgreSQL)
extern "C" {

// Version info
Datum hartonomous_version(PG_FUNCTION_ARGS);

// BLAKE3 hashing
Datum blake3_hash(PG_FUNCTION_ARGS);
Datum blake3_hash_codepoint(PG_FUNCTION_ARGS);

// Codepoint projection
Datum codepoint_to_s3(PG_FUNCTION_ARGS);
Datum codepoint_to_hilbert(PG_FUNCTION_ARGS);

// Centroid computation
Datum compute_centroid(PG_FUNCTION_ARGS);

// Text ingestion (returns stats)
Datum ingest_text(PG_FUNCTION_ARGS);

// Semantic query
Datum semantic_search(PG_FUNCTION_ARGS);

} // extern "C"
