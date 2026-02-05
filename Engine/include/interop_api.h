#pragma once

#if defined(_WIN32)
    #if defined(HARTONOMOUS_EXPORT)
        #define HARTONOMOUS_API __declspec(dllexport)
    #else
        #define HARTONOMOUS_API __declspec(dllimport)
    #endif
#else
    #define HARTONOMOUS_API __attribute__((visibility("default")))
#endif

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

// =============================================================================
//  Error Handling
// =============================================================================

// Thread-local error storage
HARTONOMOUS_API const char* hartonomous_get_last_error();
HARTONOMOUS_API const char* hartonomous_get_version();

// =============================================================================
//  Opaque Handles
// =============================================================================

typedef void* h_db_connection_t;
typedef void* h_ingester_t;
typedef void* h_godel_t;
typedef void* h_walk_engine_t;

// =============================================================================
//  Database Connection
// =============================================================================

HARTONOMOUS_API h_db_connection_t hartonomous_db_create(const char* connection_string);
HARTONOMOUS_API void hartonomous_db_destroy(h_db_connection_t handle);
HARTONOMOUS_API bool hartonomous_db_is_connected(h_db_connection_t handle);

// =============================================================================
//  Core Primitives (Hashing & Projection)
// =============================================================================

HARTONOMOUS_API void hartonomous_blake3_hash(const char* data, size_t len, uint8_t* out_16b);
HARTONOMOUS_API void hartonomous_blake3_hash_codepoint(uint32_t codepoint, uint8_t* out_16b);
HARTONOMOUS_API bool hartonomous_codepoint_to_s3(uint32_t codepoint, double* out_4d);
HARTONOMOUS_API void hartonomous_s3_to_hilbert(const double* in_4d, uint32_t entity_type, uint64_t* out_hi, uint64_t* out_lo);
HARTONOMOUS_API void hartonomous_s3_compute_centroid(const double* points_4d, size_t count, double* out_4d);

// =============================================================================
//  Ingestion Service
// =============================================================================

typedef struct HIngestionStats {
    size_t atoms_total;
    size_t atoms_new;
    size_t compositions_total;
    size_t compositions_new;
    size_t relations_total;
    size_t relations_new;
    size_t evidence_count;
    size_t original_bytes;
    size_t stored_bytes;
    double compression_ratio;
    size_t ngrams_extracted;
    size_t ngrams_significant;
    size_t cooccurrences_found;
    size_t cooccurrences_significant;
} HIngestionStats;

HARTONOMOUS_API h_ingester_t hartonomous_ingester_create(h_db_connection_t db_handle);
HARTONOMOUS_API void hartonomous_ingester_destroy(h_ingester_t handle);
HARTONOMOUS_API bool hartonomous_ingest_text(h_ingester_t handle, const char* text, HIngestionStats* out_stats);
HARTONOMOUS_API bool hartonomous_ingest_file(h_ingester_t handle, const char* file_path, HIngestionStats* out_stats);

// =============================================================================
//  Walk Engine
// =============================================================================

typedef struct HWalkParameters {
    double w_model;
    double w_text;
    double w_rel;
    double w_geo;
    double w_hilbert;
    double w_repeat;
    double w_novelty;
    double goal_attraction;
    double w_energy;
    double base_temp;
    double energy_alpha;
    double energy_decay;
    size_t context_window;
} HWalkParameters;

typedef struct HWalkState {
    uint8_t current_composition[16];
    double current_position[4];
    double current_energy;
} HWalkState;

typedef struct HWalkStepResult {
    uint8_t next_composition[16];
    double probability;
    double energy_remaining;
    bool terminated;
    char reason[256];
} HWalkStepResult;

HARTONOMOUS_API h_walk_engine_t hartonomous_walk_create(h_db_connection_t db_handle);
HARTONOMOUS_API void hartonomous_walk_destroy(h_walk_engine_t handle);
HARTONOMOUS_API bool hartonomous_walk_init(h_walk_engine_t handle, const uint8_t* start_id, double initial_energy, HWalkState* out_state);
HARTONOMOUS_API bool hartonomous_walk_step(h_walk_engine_t handle, HWalkState* in_out_state, const HWalkParameters* params, HWalkStepResult* out_result);
HARTONOMOUS_API bool hartonomous_walk_set_goal(h_walk_engine_t handle, HWalkState* in_out_state, const uint8_t* goal_id);

// =============================================================================
//  Godel Engine
// =============================================================================

// =============================================================================
//  Common Types
// =============================================================================

typedef enum {
    H_ENTITY_COMPOSITION = 0,
    H_ENTITY_ATOM = 1
} H_ENTITY_TYPE;

typedef struct HKnowledgeGap {
    char* concept_name;
    int references_count;
    double confidence;
} HKnowledgeGap;

typedef struct HSubProblem {
    uint8_t node_id[16];
    char* description;
    int difficulty;
    bool is_solvable;
} HSubProblem;

typedef struct HResearchPlan {
    char* original_problem;
    HSubProblem* sub_problems;
    size_t sub_problems_count;
    HKnowledgeGap* knowledge_gaps;
    size_t knowledge_gaps_count;
    int total_steps;
    int solvable_steps;
} HResearchPlan;

HARTONOMOUS_API h_godel_t hartonomous_godel_create(h_db_connection_t db_handle);
HARTONOMOUS_API void hartonomous_godel_destroy(h_godel_t handle);
HARTONOMOUS_API bool hartonomous_godel_analyze(h_godel_t handle, const char* problem, HResearchPlan* out_plan);
HARTONOMOUS_API void hartonomous_godel_free_plan(HResearchPlan* plan);

#ifdef __cplusplus
}
#endif
