#pragma once

/**
 * @file exports.h
 * @brief Public C API for Hartonomous native library
 *
 * This header defines the P/Invoke compatible API for accessing Unicode semantic
 * decomposition and 4D Hilbert curve encoding functionality from managed languages.
 *
 * ## Error Codes
 * All functions return an integer status code:
 * - 0: Success
 * - -1: Null pointer argument
 * - -2: Invalid codepoint (outside Unicode range or surrogate)
 * - -3: Buffer too small / capacity exceeded
 *
 * ## Thread Safety
 * All functions are thread-safe and can be called concurrently.
 *
 * ## Coordinate System
 * Uses CENTER-ORIGIN geometry with SIGNED coordinates:
 * - Origin at (0, 0, 0, 0)
 * - Range: [-INT32_MAX, +INT32_MAX] per dimension
 * - Tesseract surface faces at ±INT32_MAX
 */

#include <cstdint>

// Platform-specific export macros
#if defined(_WIN32) || defined(_WIN64)
    #ifdef HARTONOMOUS_EXPORTS
        #define HARTONOMOUS_API __declspec(dllexport)
    #else
        #define HARTONOMOUS_API __declspec(dllimport)
    #endif
#else
    #define HARTONOMOUS_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/**
 * @brief Result structure for codepoint mapping (C-compatible)
 *
 * CENTER-ORIGIN GEOMETRY: coordinates are SIGNED, origin at (0,0,0,0).
 * Surface faces are at ±INT32_MAX.
 * Fields ordered to avoid padding: 8-byte types first, then 4-byte, then 1-byte.
 */
typedef struct {
    std::int64_t hilbert_high;  ///< Upper 64 bits of 128-bit Hilbert index
    std::int64_t hilbert_low;   ///< Lower 64 bits of 128-bit Hilbert index
    std::int32_t codepoint;     ///< Original Unicode codepoint
    std::int32_t x;             ///< X coordinate, range [-INT32_MAX, +INT32_MAX]
    std::int32_t y;             ///< Y coordinate, range [-INT32_MAX, +INT32_MAX]
    std::int32_t z;             ///< Z coordinate, range [-INT32_MAX, +INT32_MAX]
    std::int32_t w;             ///< W coordinate, range [-INT32_MAX, +INT32_MAX]
    std::uint8_t face;          ///< Tesseract face index (0-7)
} HartonomousAtom;

/**
 * @brief Map a single Unicode codepoint to its tesseract surface coordinates and Hilbert index.
 * @param codepoint Unicode codepoint to map (0 to 0x10FFFF, excluding surrogates)
 * @param result Pointer to receive the mapping result
 * @return 0 on success, -1 if result is NULL, -2 if codepoint is invalid
 */
HARTONOMOUS_API int hartonomous_map_codepoint(std::int32_t codepoint, HartonomousAtom* result);

/**
 * @brief Map a range of codepoints into a pre-allocated array.
 * @param start First codepoint in range (inclusive)
 * @param end Last codepoint in range (exclusive)
 * @param results Pre-allocated array to receive results
 * @param results_capacity Maximum number of results that can be stored
 * @return Number of successfully mapped codepoints, or -1 on error
 */
HARTONOMOUS_API int hartonomous_map_codepoint_range(
    std::int32_t start,
    std::int32_t end,
    HartonomousAtom* results,
    std::int32_t results_capacity);

/**
 * @brief Get the Hilbert index for a codepoint (index only, no full mapping).
 * @param codepoint Unicode codepoint to convert
 * @param high Pointer to receive upper 64 bits of index
 * @param low Pointer to receive lower 64 bits of index
 * @return 0 on success, -1 if pointers are NULL, -2 if codepoint is invalid
 */
HARTONOMOUS_API int hartonomous_get_hilbert_index(
    std::int32_t codepoint,
    std::int64_t* high,
    std::int64_t* low);

/**
 * @brief Convert 4D coordinates to Hilbert index.
 *
 * Used for verification that managed and native code produce identical results.
 * @param x First coordinate (unsigned 32-bit)
 * @param y Second coordinate (unsigned 32-bit)
 * @param z Third coordinate (unsigned 32-bit)
 * @param w Fourth coordinate (unsigned 32-bit)
 * @param high Pointer to receive upper 64 bits of index
 * @param low Pointer to receive lower 64 bits of index
 * @return 0 on success, -1 if pointers are NULL
 */
HARTONOMOUS_API int hartonomous_coords_to_hilbert(
    std::uint32_t x, std::uint32_t y, std::uint32_t z, std::uint32_t w,
    std::int64_t* high, std::int64_t* low);

/**
 * @brief Convert Hilbert index to 4D coordinates.
 * @param high Upper 64 bits of Hilbert index
 * @param low Lower 64 bits of Hilbert index
 * @param x Pointer to receive first coordinate
 * @param y Pointer to receive second coordinate
 * @param z Pointer to receive third coordinate
 * @param w Pointer to receive fourth coordinate
 * @return 0 on success, -1 if any pointer is NULL
 */
HARTONOMOUS_API int hartonomous_hilbert_to_coords(
    std::int64_t high, std::int64_t low,
    std::uint32_t* x, std::uint32_t* y, std::uint32_t* z, std::uint32_t* w);

/**
 * @brief Check if a codepoint is a valid Unicode scalar value.
 * @param codepoint Value to check
 * @return 1 if valid scalar (0-0xD7FF or 0xE000-0x10FFFF), 0 otherwise
 */
HARTONOMOUS_API int hartonomous_is_valid_scalar(std::int32_t codepoint);

/**
 * @brief Get the total number of valid Unicode codepoints.
 * @return 1,114,112 (0x110000 - 2048 surrogates)
 */
HARTONOMOUS_API std::int32_t hartonomous_get_codepoint_count();

/**
 * @brief Get the maximum valid codepoint.
 * @return 0x10FFFF (1,114,111)
 */
HARTONOMOUS_API std::int32_t hartonomous_get_max_codepoint();

/**
 * @brief Get the library version string.
 * @return Null-terminated version string (e.g., "1.0.0")
 */
HARTONOMOUS_API const char* hartonomous_get_version();

// =============================================================================
// DATABASE OPERATIONS
// =============================================================================

/**
 * @brief Database statistics structure
 */
typedef struct {
    std::int64_t atom_count;
    std::int64_t composition_count;
    std::int64_t relationship_count;
    std::int64_t database_size_bytes;
} HartonomousDbStats;

/**
 * @brief Ingestion result structure
 */
typedef struct {
    std::int64_t files_processed;
    std::int64_t bytes_processed;
    std::int64_t compositions_created;
    std::int64_t relationships_created;
    std::int64_t errors;
    std::int64_t duration_ms;
} HartonomousIngestResult;

/**
 * @brief Initialize database connection and ensure schema exists.
 * Uses HARTONOMOUS_DB_URL env var or defaults to localhost:5432.
 * Idempotent - safe to call multiple times.
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_db_init();

/**
 * @brief Get database statistics.
 * @param stats Pointer to receive statistics
 * @return 0 on success, -1 if stats is NULL, -2 on database error
 */
HARTONOMOUS_API int hartonomous_db_stats(HartonomousDbStats* stats);

/**
 * @brief Ingest a file or directory into the substrate.
 * @param path Path to file or directory
 * @param sparsity Sparsity threshold for model weights (default 1e-6)
 * @param result Pointer to receive ingestion result (can be NULL)
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_ingest(
    const char* path,
    double sparsity,
    HartonomousIngestResult* result);

/**
 * @brief Check if content exists in the substrate.
 * @param text Text to check
 * @param text_len Length of text in bytes
 * @param exists Pointer to receive result (1 if exists, 0 if not)
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_content_exists(
    const char* text,
    std::int32_t text_len,
    int* exists);

/**
 * @brief Encode text and store in substrate.
 * @param text Text to encode
 * @param text_len Length of text in bytes
 * @param id_high Pointer to receive upper 64 bits of root ID
 * @param id_low Pointer to receive lower 64 bits of root ID
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_encode_and_store(
    const char* text,
    std::int32_t text_len,
    std::int64_t* id_high,
    std::int64_t* id_low);

/**
 * @brief Decode a root ID back to original text.
 * @param id_high Upper 64 bits of root ID
 * @param id_low Lower 64 bits of root ID
 * @param buffer Buffer to receive decoded text
 * @param buffer_capacity Size of buffer
 * @param text_len Pointer to receive actual text length
 * @return 0 on success, -1 if buffer too small, -2 if ID not found
 */
HARTONOMOUS_API int hartonomous_decode(
    std::int64_t id_high,
    std::int64_t id_low,
    char* buffer,
    std::int32_t buffer_capacity,
    std::int32_t* text_len);

// =============================================================================
// SPATIAL QUERIES - PostGIS-backed semantic proximity
// =============================================================================

/**
 * @brief Result from spatial query
 */
typedef struct {
    std::int64_t hilbert_high;
    std::int64_t hilbert_low;
    std::int32_t codepoint;
    double distance;
} HartonomousSpatialMatch;

/**
 * @brief Find atoms semantically similar to a codepoint using KNN.
 * @param codepoint Reference codepoint
 * @param results Pre-allocated array for results
 * @param capacity Maximum results to return
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_similar(
    std::int32_t codepoint,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count);

/**
 * @brief Find atoms within distance of a codepoint's semantic position.
 * Uses ST_DWithin with GIST index.
 * @param codepoint Reference codepoint
 * @param distance_threshold Maximum distance
 * @param results Pre-allocated array for results
 * @param capacity Maximum results to return
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_near(
    std::int32_t codepoint,
    double distance_threshold,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count);

/**
 * @brief Find all case variants of a character (same base, different variant).
 * 'a' finds 'A', 'à', 'á', 'À', etc.
 * @param codepoint Reference codepoint
 * @param results Pre-allocated array for results
 * @param capacity Maximum results to return
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_case_variants(
    std::int32_t codepoint,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count);

/**
 * @brief Find all diacritical variants of a base character.
 * @param codepoint Reference codepoint
 * @param results Pre-allocated array for results
 * @param capacity Maximum results to return
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_diacritical_variants(
    std::int32_t codepoint,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count);

// =============================================================================
// RELATIONSHIP QUERIES - Sparse semantic graph traversal
// =============================================================================

/**
 * @brief Relationship types
 */
typedef enum {
    HARTONOMOUS_REL_SEMANTIC_LINK = 0,
    HARTONOMOUS_REL_MODEL_WEIGHT = 1,
    HARTONOMOUS_REL_KNOWLEDGE_EDGE = 2,
    HARTONOMOUS_REL_TEMPORAL_NEXT = 3,
    HARTONOMOUS_REL_SPATIAL_NEAR = 4
} HartonomousRelType;

/**
 * @brief Relationship result
 */
typedef struct {
    std::int64_t from_high;
    std::int64_t from_low;
    std::int64_t to_high;
    std::int64_t to_low;
    double weight;
    std::int16_t rel_type;
    std::int64_t context_high;
    std::int64_t context_low;
} HartonomousRelationship;

/**
 * @brief Store a weighted relationship between two nodes.
 * Sparse: only call for salient (non-zero) weights.
 * @param from_high Upper 64 bits of source node
 * @param from_low Lower 64 bits of source node
 * @param to_high Upper 64 bits of target node
 * @param to_low Lower 64 bits of target node
 * @param weight Relationship weight
 * @param rel_type Relationship type
 * @param context_high Upper 64 bits of context (e.g., model ID)
 * @param context_low Lower 64 bits of context
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_store_relationship(
    std::int64_t from_high, std::int64_t from_low,
    std::int64_t to_high, std::int64_t to_low,
    double weight,
    std::int16_t rel_type,
    std::int64_t context_high, std::int64_t context_low);

/**
 * @brief Find relationships FROM a node (outgoing edges).
 * @param from_high Upper 64 bits of source node
 * @param from_low Lower 64 bits of source node
 * @param results Pre-allocated array for results
 * @param capacity Maximum results to return
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_from(
    std::int64_t from_high, std::int64_t from_low,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count);

/**
 * @brief Find relationships TO a node (incoming edges).
 * @param to_high Upper 64 bits of target node
 * @param to_low Lower 64 bits of target node
 * @param results Pre-allocated array for results
 * @param capacity Maximum results to return
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_to(
    std::int64_t to_high, std::int64_t to_low,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count);

/**
 * @brief Find relationships by type from a node.
 * @param from_high Upper 64 bits of source node
 * @param from_low Lower 64 bits of source node
 * @param rel_type Relationship type to filter by
 * @param results Pre-allocated array for results
 * @param capacity Maximum results to return
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_by_type(
    std::int64_t from_high, std::int64_t from_low,
    std::int16_t rel_type,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count);

/**
 * @brief Find relationships by weight range.
 * @param min_weight Minimum weight (inclusive)
 * @param max_weight Maximum weight (inclusive)
 * @param context_high Upper 64 bits of context (0 for any)
 * @param context_low Lower 64 bits of context (0 for any)
 * @param results Pre-allocated array for results
 * @param capacity Maximum results to return
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_by_weight(
    double min_weight, double max_weight,
    std::int64_t context_high, std::int64_t context_low,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count);

/**
 * @brief Get weight between two specific nodes.
 * @param from_high Upper 64 bits of source node
 * @param from_low Lower 64 bits of source node
 * @param to_high Upper 64 bits of target node
 * @param to_low Lower 64 bits of target node
 * @param context_high Upper 64 bits of context
 * @param context_low Lower 64 bits of context
 * @param weight Pointer to receive weight (or NaN if not found)
 * @return 0 on success, 1 if not found, negative on error
 */
HARTONOMOUS_API int hartonomous_get_weight(
    std::int64_t from_high, std::int64_t from_low,
    std::int64_t to_high, std::int64_t to_low,
    std::int64_t context_high, std::int64_t context_low,
    double* weight);

// =============================================================================
// TRAJECTORY QUERIES - RLE-compressed paths through semantic space
// =============================================================================

/**
 * @brief RLE-compressed trajectory point
 */
typedef struct {
    std::int16_t page;      ///< X: Unicode page
    std::int16_t type;      ///< Y: Character type
    std::int32_t base;      ///< Z: Base character
    std::uint8_t variant;   ///< M: Variant (case/diacritical)
    std::uint32_t count;    ///< RLE: repetition count
} HartonomousTrajectoryPoint;

/**
 * @brief Build RLE-compressed trajectory from text.
 * "Hello" → H(1), e(1), l(2), o(1)
 * @param text Input text
 * @param text_len Length of text
 * @param points Pre-allocated array for trajectory points
 * @param capacity Maximum points to return
 * @param point_count Pointer to receive actual point count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_build_trajectory(
    const char* text,
    std::int32_t text_len,
    HartonomousTrajectoryPoint* points,
    std::int32_t capacity,
    std::int32_t* point_count);

/**
 * @brief Store trajectory as relationship between nodes.
 * @param from_high Upper 64 bits of source node
 * @param from_low Lower 64 bits of source node
 * @param to_high Upper 64 bits of target node
 * @param to_low Lower 64 bits of target node
 * @param points Trajectory points
 * @param point_count Number of points
 * @param weight Trajectory weight
 * @param rel_type Relationship type
 * @param context_high Upper 64 bits of context
 * @param context_low Lower 64 bits of context
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_store_trajectory(
    std::int64_t from_high, std::int64_t from_low,
    std::int64_t to_high, std::int64_t to_low,
    const HartonomousTrajectoryPoint* points,
    std::int32_t point_count,
    double weight,
    std::int16_t rel_type,
    std::int64_t context_high, std::int64_t context_low);

/**
 * @brief Convert trajectory back to text (expanding RLE).
 * @param points Trajectory points
 * @param point_count Number of points
 * @param buffer Buffer to receive text
 * @param buffer_capacity Size of buffer
 * @param text_len Pointer to receive actual text length
 * @return 0 on success, -1 if buffer too small
 */
HARTONOMOUS_API int hartonomous_trajectory_to_text(
    const HartonomousTrajectoryPoint* points,
    std::int32_t point_count,
    char* buffer,
    std::int32_t buffer_capacity,
    std::int32_t* text_len);

// =============================================================================
// CONTAINMENT QUERIES - Substring and composition search
// =============================================================================

/**
 * @brief Check if substring exists in any stored content.
 * @param text Substring to search for
 * @param text_len Length of substring
 * @param exists Pointer to receive result (1 if found, 0 if not)
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_contains_substring(
    const char* text,
    std::int32_t text_len,
    int* exists);

/**
 * @brief Find compositions containing a substring.
 * Returns roots of compositions that contain the substring.
 * @param text Substring to search for
 * @param text_len Length of substring
 * @param results Pre-allocated array for root IDs (pairs of high/low)
 * @param capacity Maximum results (number of pairs)
 * @param count Pointer to receive actual result count
 * @return 0 on success, negative on error
 */
HARTONOMOUS_API int hartonomous_find_containing(
    const char* text,
    std::int32_t text_len,
    std::int64_t* results,  // Array of [high0, low0, high1, low1, ...]
    std::int32_t capacity,
    std::int32_t* count);

#ifdef __cplusplus
}
#endif
