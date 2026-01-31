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

extern "C" {

// =============================================================================
//  Error Handling
// =============================================================================

// Thread-local error storage
HARTONOMOUS_API const char* hartonomous_get_last_error();

// =============================================================================
//  Opaque Handles
// =============================================================================

typedef void* h_db_connection_t;
typedef void* h_ingester_t;
typedef void* h_godel_t;

// =============================================================================
//  Database Connection
// =============================================================================

HARTONOMOUS_API h_db_connection_t hartonomous_db_create(const char* connection_string);
HARTONOMOUS_API void hartonomous_db_destroy(h_db_connection_t handle);
HARTONOMOUS_API bool hartonomous_db_is_connected(h_db_connection_t handle);

// =============================================================================
//  Ingestion Service
// =============================================================================

struct HIngestionStats {
    size_t atoms_total;
    size_t atoms_new;
    size_t compositions_total;
    size_t compositions_new;
    size_t relations_total;
    size_t relations_new;
    size_t evidence_count;
    size_t original_bytes;
    size_t ngrams_extracted;
    size_t ngrams_significant;
    size_t cooccurrences_found;
    size_t cooccurrences_significant;
};

HARTONOMOUS_API h_ingester_t hartonomous_ingester_create(h_db_connection_t db_handle);
HARTONOMOUS_API void hartonomous_ingester_destroy(h_ingester_t handle);
HARTONOMOUS_API bool hartonomous_ingest_text(h_ingester_t handle, const char* text, HIngestionStats* out_stats);
HARTONOMOUS_API bool hartonomous_ingest_file(h_ingester_t handle, const char* file_path, HIngestionStats* out_stats);

// =============================================================================
//  Godel Engine
// =============================================================================

struct HKnowledgeGap {
    char* concept_name;
    int references_count;
    double confidence;
};

struct HSubProblem {
    uint64_t node_id;
    char* description;
    int difficulty;
    bool is_solvable;
};

struct HResearchPlan {
    char* original_problem;
    HSubProblem* sub_problems;
    size_t sub_problems_count;
    HKnowledgeGap* knowledge_gaps;
    size_t knowledge_gaps_count;
    int total_steps;
    int solvable_steps;
};

HARTONOMOUS_API h_godel_t hartonomous_godel_create(h_db_connection_t db_handle);
HARTONOMOUS_API void hartonomous_godel_destroy(h_godel_t handle);
HARTONOMOUS_API bool hartonomous_godel_analyze(h_godel_t handle, const char* problem, HResearchPlan* out_plan);
HARTONOMOUS_API void hartonomous_godel_free_plan(HResearchPlan* plan);

}
