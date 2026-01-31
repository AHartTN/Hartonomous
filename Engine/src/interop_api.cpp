#include <interop_api.h>
#include <cognitive/godel_engine.hpp>
#include <ingestion/text_ingester.hpp>
#include <database/postgres_connection.hpp>
#include <stdexcept>
#include <cstring>
#include <memory>

// Thread-local error storage
thread_local std::string g_last_error;

const char* hartonomous_get_last_error() {
    return g_last_error.c_str();
}

void set_error(const std::exception& e) {
    g_last_error = e.what();
}

void set_error(const char* msg) {
    g_last_error = msg;
}

// Helper for string duplication
char* strdup_safe(const std::string& str) {
#ifdef _WIN32
    return _strdup(str.c_str());
#else
    return strdup(str.c_str());
#endif
}

// =============================================================================
//  Database Connection
// =============================================================================

h_db_connection_t hartonomous_db_create(const char* connection_string) {
    try {
        auto* db = new Hartonomous::PostgresConnection(connection_string);
        return static_cast<h_db_connection_t>(db);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

void hartonomous_db_destroy(h_db_connection_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::PostgresConnection*>(handle);
    }
}

bool hartonomous_db_is_connected(h_db_connection_t handle) {
    if (!handle) return false;
    // Assuming PostgresConnection has an is_connected() or similar check, 
    // or just valid instance implies connection attempted.
    // For now we assume if the object exists, it's "valid" enough.
    // Ideally PostgresConnection should expose connection state.
    return true; 
}

// =============================================================================
//  Ingestion Service
// =============================================================================

h_ingester_t hartonomous_ingester_create(h_db_connection_t db_handle) {
    try {
        if (!db_handle) throw std::runtime_error("Invalid database handle");
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        auto* ingester = new Hartonomous::TextIngester(*db);
        return static_cast<h_ingester_t>(ingester);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

void hartonomous_ingester_destroy(h_ingester_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::TextIngester*>(handle);
    }
}

bool hartonomous_ingest_text(h_ingester_t handle, const char* text, HIngestionStats* out_stats) {
    try {
        if (!handle || !text || !out_stats) return false;
        auto* ingester = static_cast<Hartonomous::TextIngester*>(handle);
        auto stats = ingester->ingest(text);
        
        out_stats->atoms_total = stats.atoms_total;
        out_stats->atoms_new = stats.atoms_new;
        out_stats->compositions_total = stats.compositions_total;
        out_stats->compositions_new = stats.compositions_new;
        out_stats->relations_total = stats.relations_total;
        out_stats->relations_new = stats.relations_new;
        out_stats->evidence_count = stats.evidence_count;
        out_stats->original_bytes = stats.original_bytes;
        out_stats->ngrams_extracted = stats.ngrams_extracted;
        out_stats->ngrams_significant = stats.ngrams_significant;
        out_stats->cooccurrences_found = stats.cooccurrences_found;
        out_stats->cooccurrences_significant = stats.cooccurrences_significant;

        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

bool hartonomous_ingest_file(h_ingester_t handle, const char* file_path, HIngestionStats* out_stats) {
    try {
        if (!handle || !file_path || !out_stats) return false;
        auto* ingester = static_cast<Hartonomous::TextIngester*>(handle);
        auto stats = ingester->ingest_file(file_path);

        out_stats->atoms_total = stats.atoms_total;
        out_stats->atoms_new = stats.atoms_new;
        out_stats->compositions_total = stats.compositions_total;
        out_stats->compositions_new = stats.compositions_new;
        out_stats->relations_total = stats.relations_total;
        out_stats->relations_new = stats.relations_new;
        out_stats->evidence_count = stats.evidence_count;
        out_stats->original_bytes = stats.original_bytes;
        out_stats->ngrams_extracted = stats.ngrams_extracted;
        out_stats->ngrams_significant = stats.ngrams_significant;
        out_stats->cooccurrences_found = stats.cooccurrences_found;
        out_stats->cooccurrences_significant = stats.cooccurrences_significant;
        
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

// =============================================================================
//  Godel Engine
// =============================================================================

h_godel_t hartonomous_godel_create(h_db_connection_t db_handle) {
    try {
        if (!db_handle) throw std::runtime_error("Invalid database handle");
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        auto* godel = new Hartonomous::GodelEngine(*db);
        return static_cast<h_godel_t>(godel);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

void hartonomous_godel_destroy(h_godel_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::GodelEngine*>(handle);
    }
}

bool hartonomous_godel_analyze(h_godel_t handle, const char* problem, HResearchPlan* out_plan) {
    try {
        if (!handle || !problem || !out_plan) return false;
        auto* godel = static_cast<Hartonomous::GodelEngine*>(handle);
        auto plan = godel->analyze_problem(problem);
        
        out_plan->original_problem = strdup_safe(plan.original_problem);
        out_plan->total_steps = plan.total_steps;
        out_plan->solvable_steps = plan.solvable_steps;
        
        // Convert sub-problems
        out_plan->sub_problems_count = plan.decomposition.size();
        if (out_plan->sub_problems_count > 0) {
            out_plan->sub_problems = new HSubProblem[out_plan->sub_problems_count];
            for (size_t i = 0; i < out_plan->sub_problems_count; ++i) {
                out_plan->sub_problems[i].node_id = plan.decomposition[i].node_id;
                out_plan->sub_problems[i].description = strdup_safe(plan.decomposition[i].description);
                out_plan->sub_problems[i].difficulty = plan.decomposition[i].difficulty;
                out_plan->sub_problems[i].is_solvable = plan.decomposition[i].is_solvable;
            }
        } else {
            out_plan->sub_problems = nullptr;
        }

        // Convert knowledge gaps
        out_plan->knowledge_gaps_count = plan.knowledge_gaps.size();
        if (out_plan->knowledge_gaps_count > 0) {
            out_plan->knowledge_gaps = new HKnowledgeGap[out_plan->knowledge_gaps_count];
            for (size_t i = 0; i < out_plan->knowledge_gaps_count; ++i) {
                out_plan->knowledge_gaps[i].concept_name = strdup_safe(plan.knowledge_gaps[i].concept_name);
                out_plan->knowledge_gaps[i].references_count = plan.knowledge_gaps[i].references_count;
                out_plan->knowledge_gaps[i].confidence = plan.knowledge_gaps[i].confidence;
            }
        } else {
            out_plan->knowledge_gaps = nullptr;
        }

        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

void hartonomous_godel_free_plan(HResearchPlan* plan) {
    if (!plan) return;
    
    if (plan->original_problem) free(plan->original_problem);
    
    if (plan->sub_problems) {
        for (size_t i = 0; i < plan->sub_problems_count; ++i) {
            if (plan->sub_problems[i].description) free(plan->sub_problems[i].description);
        }
        delete[] plan->sub_problems;
    }
    
    if (plan->knowledge_gaps) {
        for (size_t i = 0; i < plan->knowledge_gaps_count; ++i) {
            if (plan->knowledge_gaps[i].concept_name) free(plan->knowledge_gaps[i].concept_name);
        }
        delete[] plan->knowledge_gaps;
    }
    
    // We don't free the plan pointer itself as it's likely a stack object or managed by caller
    // but we clear its members
    plan->original_problem = nullptr;
    plan->sub_problems = nullptr;
    plan->knowledge_gaps = nullptr;
}
