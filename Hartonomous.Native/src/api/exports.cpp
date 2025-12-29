#include "exports.h"
#include "../unicode/codepoint_mapper.hpp"

#ifdef HARTONOMOUS_HAS_DATABASE
#include "../db/query_store.hpp"
#include "../db/database_store.hpp"
#include "../db/seeder.hpp"
#include "../model/model_ingest.hpp"
#include "../mlops/mlops.hpp"
#include "../atoms/database_encoder.hpp"
#include <memory>
#include <mutex>
#include <filesystem>
#include <chrono>
#include <fstream>
#endif

using namespace hartonomous;

static constexpr const char* VERSION = "0.1.0";

#ifdef HARTONOMOUS_HAS_DATABASE
using namespace hartonomous::db;
using namespace hartonomous::model;

// Thread-safe singleton for database connection
static std::mutex g_db_mutex;
static std::atomic<bool> g_initialized{false};
static std::unique_ptr<QueryStore> g_store;
static std::unique_ptr<DatabaseStore> g_db_store;  // For fast ingestion

static QueryStore& get_store() {
    std::lock_guard<std::mutex> lock(g_db_mutex);
    if (!g_store) {
        g_store = std::make_unique<QueryStore>();
    }
    return *g_store;
}

static DatabaseStore& get_db_store() {
    std::lock_guard<std::mutex> lock(g_db_mutex);
    if (!g_db_store) {
        g_db_store = std::make_unique<DatabaseStore>();
    }
    return *g_db_store;
}
#endif

extern "C" {

HARTONOMOUS_API int hartonomous_map_codepoint(std::int32_t codepoint, HartonomousAtom* result) {
    if (!result) return -1;
    if (codepoint < 0 || codepoint > CodepointMapper::MAX_CODEPOINT) return -2;

    auto mapping = CodepointMapper::map(codepoint);
    
    result->hilbert_high = static_cast<std::int64_t>(mapping.hilbert_index.high);
    result->hilbert_low = static_cast<std::int64_t>(mapping.hilbert_index.low);
    result->codepoint = mapping.codepoint;
    result->x = mapping.surface_point.x;
    result->y = mapping.surface_point.y;
    result->z = mapping.surface_point.z;
    result->w = mapping.surface_point.w;
    result->face = static_cast<std::uint8_t>(mapping.surface_point.face);

    return 0;
}

HARTONOMOUS_API int hartonomous_map_codepoint_range(
    std::int32_t start,
    std::int32_t end,
    HartonomousAtom* results,
    std::int32_t results_capacity)
{
    if (!results || results_capacity <= 0) return 0;
    if (start < 0) start = 0;
    if (end > CodepointMapper::MAX_CODEPOINT) end = CodepointMapper::MAX_CODEPOINT;
    if (start > end) return 0;

    std::int32_t count = 0;
    for (std::int32_t cp = start; cp <= end && count < results_capacity; ++cp) {
        auto mapping = CodepointMapper::map(cp);
        
        results[count].hilbert_high = static_cast<std::int64_t>(mapping.hilbert_index.high);
        results[count].hilbert_low = static_cast<std::int64_t>(mapping.hilbert_index.low);
        results[count].codepoint = mapping.codepoint;
        results[count].x = mapping.surface_point.x;
        results[count].y = mapping.surface_point.y;
        results[count].z = mapping.surface_point.z;
        results[count].w = mapping.surface_point.w;
        results[count].face = static_cast<std::uint8_t>(mapping.surface_point.face);
        
        ++count;
    }

    return count;
}

HARTONOMOUS_API int hartonomous_get_hilbert_index(
    std::int32_t codepoint,
    std::int64_t* high,
    std::int64_t* low)
{
    if (!high || !low) return -1;
    if (codepoint < 0 || codepoint > CodepointMapper::MAX_CODEPOINT) return -2;

    auto index = CodepointMapper::get_hilbert_index(codepoint);
    *high = static_cast<std::int64_t>(index.high);
    *low = static_cast<std::int64_t>(index.low);

    return 0;
}

HARTONOMOUS_API int hartonomous_coords_to_hilbert(
    std::uint32_t x, std::uint32_t y, std::uint32_t z, std::uint32_t w,
    std::int64_t* high, std::int64_t* low)
{
    if (!high || !low) return -1;

    auto index = HilbertCurve4D::coords_to_index(x, y, z, w);
    *high = static_cast<std::int64_t>(index.high);
    *low = static_cast<std::int64_t>(index.low);

    return 0;
}

HARTONOMOUS_API int hartonomous_hilbert_to_coords(
    std::int64_t high, std::int64_t low,
    std::uint32_t* x, std::uint32_t* y, std::uint32_t* z, std::uint32_t* w)
{
    if (!x || !y || !z || !w) return -1;

    UInt128 index{static_cast<std::uint64_t>(high), static_cast<std::uint64_t>(low)};
    auto coords = HilbertCurve4D::index_to_coords(index);
    
    *x = coords[0];
    *y = coords[1];
    *z = coords[2];
    *w = coords[3];

    return 0;
}

HARTONOMOUS_API int hartonomous_is_valid_scalar(std::int32_t codepoint) {
    return CodepointMapper::is_valid_scalar(codepoint) ? 1 : 0;
}

HARTONOMOUS_API std::int32_t hartonomous_get_codepoint_count() {
    return CodepointMapper::CODEPOINT_COUNT;
}

HARTONOMOUS_API std::int32_t hartonomous_get_max_codepoint() {
    return CodepointMapper::MAX_CODEPOINT;
}

HARTONOMOUS_API const char* hartonomous_get_version() {
    return VERSION;
}

// =============================================================================
// DATABASE OPERATIONS (only available when compiled with PostgreSQL)
// =============================================================================

#ifdef HARTONOMOUS_HAS_DATABASE

HARTONOMOUS_API int hartonomous_db_init() {
    // Double-checked locking with atomic flag
    if (g_initialized.load(std::memory_order_acquire)) {
        return 0;
    }
    
    try {
        std::lock_guard<std::mutex> lock(g_db_mutex);
        if (!g_initialized.load(std::memory_order_relaxed)) {
            Seeder seeder(true);  // quiet mode
            seeder.ensure_schema();
            g_store = std::make_unique<QueryStore>();
            g_initialized.store(true, std::memory_order_release);
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_db_stats(HartonomousDbStats* stats) {
    if (!stats) return -1;
    
    try {
        auto& store = get_store();
        stats->atom_count = static_cast<std::int64_t>(store.atom_count());
        stats->composition_count = static_cast<std::int64_t>(store.composition_count());
        stats->relationship_count = static_cast<std::int64_t>(store.relationship_count());
        stats->database_size_bytes = store.database_size();
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_ingest(
    const char* path,
    double sparsity,
    HartonomousIngestResult* result)
{
    if (!path) return -1;
    
    try {
        // Ensure initialized
        hartonomous_db_init();
        
        auto& db = get_db_store();
        auto& store = get_store();
        auto start = std::chrono::high_resolution_clock::now();
        
        std::filesystem::path p(path);
        std::int64_t files = 0;
        std::int64_t bytes = 0;
        std::int64_t compositions = 0;
        std::int64_t relationships = 0;
        std::int64_t errors = 0;
        
        if (std::filesystem::is_directory(p)) {
            // Check if model package (recursive search for HuggingFace cache structure)
            bool has_tokenizer = false;
            bool has_safetensor = false;
            for (const auto& entry : std::filesystem::recursive_directory_iterator(p)) {
                if (!entry.is_regular_file()) continue;
                std::string name = entry.path().filename().string();
                if (name == "tokenizer.json" || name == "vocab.txt") has_tokenizer = true;
                if (entry.path().extension() == ".safetensors") has_safetensor = true;
                if (has_tokenizer && has_safetensor) break;  // Found both, stop searching
            }
            
            if (has_tokenizer && has_safetensor) {
                // Model package
                ModelIngester ingester(store, sparsity);
                auto model_result = ingester.ingest_package(path);
                compositions = static_cast<std::int64_t>(model_result.stored_weights);
                files = static_cast<std::int64_t>(model_result.tensor_count + 1);
            } else {
                // Regular directory - ingest all files using fast encoder
                DatabaseEncoder encoder(db);
                for (const auto& entry : std::filesystem::recursive_directory_iterator(p)) {
                    if (entry.is_regular_file()) {
                        try {
                            auto content_size = entry.file_size();
                            std::ifstream file(entry.path(), std::ios::binary);
                            std::vector<std::uint8_t> content(content_size);
                            file.read(reinterpret_cast<char*>(content.data()), content_size);
                            encoder.ingest(content.data(), content.size());
                            files++;
                            bytes += static_cast<std::int64_t>(content_size);
                            compositions += static_cast<std::int64_t>(encoder.composition_count());
                        } catch (...) {
                            errors++;
                        }
                    }
                }
            }
        } else if (std::filesystem::is_regular_file(p)) {
            // Single file - use fast DatabaseEncoder
            std::ifstream file(p, std::ios::binary);
            std::vector<std::uint8_t> content((std::istreambuf_iterator<char>(file)),
                                              std::istreambuf_iterator<char>());
            
            if (p.extension() == ".safetensors") {
                ModelIngester ingester(store, sparsity);
                std::vector<std::tuple<NodeRef, NodeRef, double>> all_weights;
                auto [tensors, total, stored] = ingester.ingest_safetensor_semantic(path, all_weights);
                if (!all_weights.empty()) {
                    store.store_model_weights(all_weights, ingester.model_context(), db::REL_DEFAULT);
                }
                compositions = static_cast<std::int64_t>(stored);
            } else {
                DatabaseEncoder encoder(db);
                encoder.ingest(content.data(), content.size());
                compositions = static_cast<std::int64_t>(encoder.composition_count());
                relationships = static_cast<std::int64_t>(encoder.relationship_count());
            }
            files = 1;
            bytes = static_cast<std::int64_t>(content.size());
        } else {
            return -3;  // Path doesn't exist
        }
        
        auto end = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);
        
        if (result) {
            result->files_processed = files;
            result->bytes_processed = bytes;
            result->compositions_created = compositions;
            result->relationships_created = relationships;
            result->errors = errors;
            result->duration_ms = duration.count();
        }
        
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "[hartonomous_ingest ERROR] " << e.what() << std::endl;
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_content_exists(
    const char* text,
    std::int32_t text_len,
    int* exists)
{
    if (!text || !exists) return -1;
    
    try {
        auto& store = get_store();
        auto root = store.compute_root(std::string(text, static_cast<size_t>(text_len)));
        *exists = store.exists(root) ? 1 : 0;
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_encode_and_store(
    const char* text,
    std::int32_t text_len,
    std::int64_t* id_high,
    std::int64_t* id_low)
{
    if (!text || !id_high || !id_low) return -1;
    
    try {
        // Ensure initialized
        hartonomous_db_init();
        
        auto& store = get_store();
        auto root = store.encode_and_store(std::string(text, static_cast<size_t>(text_len)));
        *id_high = root.id_high;
        *id_low = root.id_low;
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_decode(
    std::int64_t id_high,
    std::int64_t id_low,
    char* buffer,
    std::int32_t buffer_capacity,
    std::int32_t* text_len)
{
    if (!buffer || !text_len) return -1;
    
    try {
        auto& store = get_store();
        NodeRef root = NodeRef::comp(id_high, id_low);
        auto decoded = store.decode(root);
        
        *text_len = static_cast<std::int32_t>(decoded.size());
        
        if (static_cast<std::int32_t>(decoded.size()) > buffer_capacity) {
            return -1;  // Buffer too small
        }
        
        std::memcpy(buffer, decoded.data(), decoded.size());
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

// =============================================================================
// SPATIAL QUERIES
// =============================================================================

HARTONOMOUS_API int hartonomous_find_similar(
    std::int32_t codepoint,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        auto matches = store.find_similar(codepoint, static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(matches.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i].hilbert_high = matches[i].hilbert_high;
            results[i].hilbert_low = matches[i].hilbert_low;
            results[i].codepoint = matches[i].codepoint;
            results[i].distance = matches[i].distance;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_find_near(
    std::int32_t codepoint,
    double distance_threshold,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        auto matches = store.find_near_codepoint(codepoint, distance_threshold, 
                                                  static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(matches.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i].hilbert_high = matches[i].hilbert_high;
            results[i].hilbert_low = matches[i].hilbert_low;
            results[i].codepoint = matches[i].codepoint;
            results[i].distance = matches[i].distance;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_find_case_variants(
    std::int32_t codepoint,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        auto matches = store.find_case_variants(codepoint);
        
        *count = static_cast<std::int32_t>(std::min(matches.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i].hilbert_high = matches[i].hilbert_high;
            results[i].hilbert_low = matches[i].hilbert_low;
            results[i].codepoint = matches[i].codepoint;
            results[i].distance = matches[i].distance;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_find_diacritical_variants(
    std::int32_t codepoint,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        auto matches = store.find_diacritical_variants(codepoint);
        
        *count = static_cast<std::int32_t>(std::min(matches.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i].hilbert_high = matches[i].hilbert_high;
            results[i].hilbert_low = matches[i].hilbert_low;
            results[i].codepoint = matches[i].codepoint;
            results[i].distance = matches[i].distance;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

// =============================================================================
// RELATIONSHIP QUERIES
// =============================================================================

HARTONOMOUS_API int hartonomous_store_relationship(
    std::int64_t from_high, std::int64_t from_low,
    std::int64_t to_high, std::int64_t to_low,
    double weight,
    std::int16_t rel_type,
    std::int64_t context_high, std::int64_t context_low)
{
    try {
        auto& store = get_store();
        NodeRef from = NodeRef::comp(from_high, from_low);
        NodeRef to = NodeRef::comp(to_high, to_low);
        NodeRef context = NodeRef::comp(context_high, context_low);
        store.store_relationship(from, to, weight, static_cast<RelType>(rel_type), context);
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_find_from(
    std::int64_t from_high, std::int64_t from_low,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        NodeRef from = NodeRef::comp(from_high, from_low);
        auto rels = store.find_from(from, static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(rels.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i].from_high = rels[i].from.id_high;
            results[i].from_low = rels[i].from.id_low;
            results[i].to_high = rels[i].to.id_high;
            results[i].to_low = rels[i].to.id_low;
            results[i].weight = rels[i].weight;
            results[i].rel_type = rels[i].rel_type;
            results[i].context_high = rels[i].context.id_high;
            results[i].context_low = rels[i].context.id_low;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_find_to(
    std::int64_t to_high, std::int64_t to_low,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        NodeRef to = NodeRef::comp(to_high, to_low);
        auto rels = store.find_to(to, static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(rels.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i].from_high = rels[i].from.id_high;
            results[i].from_low = rels[i].from.id_low;
            results[i].to_high = rels[i].to.id_high;
            results[i].to_low = rels[i].to.id_low;
            results[i].weight = rels[i].weight;
            results[i].rel_type = rels[i].rel_type;
            results[i].context_high = rels[i].context.id_high;
            results[i].context_low = rels[i].context.id_low;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_find_by_type(
    std::int64_t from_high, std::int64_t from_low,
    std::int16_t rel_type,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        NodeRef from = NodeRef::comp(from_high, from_low);
        auto rels = store.find_by_type(from, static_cast<RelType>(rel_type), 
                                        static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(rels.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i].from_high = rels[i].from.id_high;
            results[i].from_low = rels[i].from.id_low;
            results[i].to_high = rels[i].to.id_high;
            results[i].to_low = rels[i].to.id_low;
            results[i].weight = rels[i].weight;
            results[i].rel_type = rels[i].rel_type;
            results[i].context_high = rels[i].context.id_high;
            results[i].context_low = rels[i].context.id_low;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_find_by_weight(
    double min_weight, double max_weight,
    std::int64_t context_high, std::int64_t context_low,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        NodeRef context = NodeRef::comp(context_high, context_low);
        auto rels = store.find_by_weight(min_weight, max_weight, context, 
                                          static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(rels.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i].from_high = rels[i].from.id_high;
            results[i].from_low = rels[i].from.id_low;
            results[i].to_high = rels[i].to.id_high;
            results[i].to_low = rels[i].to.id_low;
            results[i].weight = rels[i].weight;
            results[i].rel_type = rels[i].rel_type;
            results[i].context_high = rels[i].context.id_high;
            results[i].context_low = rels[i].context.id_low;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_get_weight(
    std::int64_t from_high, std::int64_t from_low,
    std::int64_t to_high, std::int64_t to_low,
    std::int64_t context_high, std::int64_t context_low,
    double* weight)
{
    if (!weight) return -1;
    
    try {
        auto& store = get_store();
        NodeRef from = NodeRef::comp(from_high, from_low);
        NodeRef to = NodeRef::comp(to_high, to_low);
        NodeRef context = NodeRef::comp(context_high, context_low);
        auto result = store.get_weight(from, to, context);
        
        if (result) {
            *weight = *result;
            return 0;
        } else {
            *weight = std::nan("");
            return 1;  // Not found
        }
    } catch (const std::exception&) {
        return -2;
    }
}

// =============================================================================
// TRAJECTORY QUERIES
// =============================================================================

HARTONOMOUS_API int hartonomous_build_trajectory(
    const char* text,
    std::int32_t text_len,
    HartonomousTrajectoryPoint* points,
    std::int32_t capacity,
    std::int32_t* point_count)
{
    if (!text || !points || !point_count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        auto traj = store.build_trajectory(std::string(text, static_cast<std::size_t>(text_len)));
        
        *point_count = static_cast<std::int32_t>(std::min(traj.points.size(), 
                                                          static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *point_count; ++i) {
            points[i].page = traj.points[i].page;
            points[i].type = traj.points[i].type;
            points[i].base = traj.points[i].base;
            points[i].variant = traj.points[i].variant;
            points[i].count = traj.points[i].count;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_store_trajectory(
    std::int64_t from_high, std::int64_t from_low,
    std::int64_t to_high, std::int64_t to_low,
    const HartonomousTrajectoryPoint* points,
    std::int32_t point_count,
    double weight,
    std::int16_t rel_type,
    std::int64_t context_high, std::int64_t context_low)
{
    if (!points || point_count <= 0) return -1;
    
    try {
        auto& store = get_store();
        NodeRef from = NodeRef::comp(from_high, from_low);
        NodeRef to = NodeRef::comp(to_high, to_low);
        NodeRef context = NodeRef::comp(context_high, context_low);
        
        // Convert to internal Trajectory
        Trajectory traj;
        traj.weight = weight;
        traj.points.reserve(static_cast<std::size_t>(point_count));
        for (std::int32_t i = 0; i < point_count; ++i) {
            TrajectoryPoint pt;
            pt.page = points[i].page;
            pt.type = points[i].type;
            pt.base = points[i].base;
            pt.variant = points[i].variant;
            pt.count = points[i].count;
            traj.points.push_back(pt);
        }
        
        store.store_trajectory(from, to, traj, static_cast<RelType>(rel_type), context);
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_trajectory_to_text(
    const HartonomousTrajectoryPoint* points,
    std::int32_t point_count,
    char* buffer,
    std::int32_t buffer_capacity,
    std::int32_t* text_len)
{
    if (!points || !buffer || !text_len || buffer_capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        
        // Convert to internal Trajectory
        Trajectory traj;
        traj.points.reserve(static_cast<std::size_t>(point_count));
        for (std::int32_t i = 0; i < point_count; ++i) {
            TrajectoryPoint pt;
            pt.page = points[i].page;
            pt.type = points[i].type;
            pt.base = points[i].base;
            pt.variant = points[i].variant;
            pt.count = points[i].count;
            traj.points.push_back(pt);
        }
        
        std::string text = store.trajectory_to_text(traj);
        *text_len = static_cast<std::int32_t>(text.size());
        
        if (static_cast<std::int32_t>(text.size()) > buffer_capacity) {
            return -1;  // Buffer too small
        }
        
        std::memcpy(buffer, text.data(), text.size());
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

// =============================================================================
// CONTAINMENT QUERIES
// =============================================================================

HARTONOMOUS_API int hartonomous_contains_substring(
    const char* text,
    std::int32_t text_len,
    int* exists)
{
    if (!text || !exists) return -1;
    
    try {
        auto& store = get_store();
        *exists = store.contains_substring(std::string(text, static_cast<std::size_t>(text_len))) ? 1 : 0;
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_find_containing(
    const char* text,
    std::int32_t text_len,
    std::int64_t* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!text || !results || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        auto matches = store.find_containing(std::string(text, static_cast<std::size_t>(text_len)),
                                              static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(matches.size(), static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            results[i * 2] = matches[i].id_high;
            results[i * 2 + 1] = matches[i].id_low;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

// =============================================================================
// MLOPS - AI INFERENCE REPLACEMENT
// =============================================================================

HARTONOMOUS_API int hartonomous_generate(
    std::int64_t context_high, std::int64_t context_low,
    std::int64_t model_high, std::int64_t model_low,
    HartonomousCandidate* candidates,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!candidates || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        mlops::MLOps ops(store);
        
        NodeRef context;
        context.id_high = context_high;
        context.id_low = context_low;
        context.is_atom = store.is_atom(context_high, context_low);
        
        NodeRef model;
        model.id_high = model_high;
        model.id_low = model_low;
        
        auto result = ops.generate(context, model, static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(result.candidates.size(), 
                                                     static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            const auto& c = result.candidates[i];
            candidates[i].ref_high = c.ref.id_high;
            candidates[i].ref_low = c.ref.id_low;
            candidates[i].probability = c.probability;
            candidates[i].log_prob = c.log_prob;
            candidates[i].is_atom = c.ref.is_atom ? 1 : 0;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_generate_next(
    std::int64_t context_high, std::int64_t context_low,
    double temperature,
    std::uint64_t seed,
    std::int64_t* result_high, std::int64_t* result_low)
{
    if (!result_high || !result_low) return -1;
    
    try {
        auto& store = get_store();
        mlops::MLOps ops(store);
        
        NodeRef context;
        context.id_high = context_high;
        context.id_low = context_low;
        context.is_atom = store.is_atom(context_high, context_low);
        
        auto next = ops.generate_next(context, temperature, seed);
        *result_high = next.id_high;
        *result_low = next.id_low;
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_infer(
    std::int64_t input_high, std::int64_t input_low,
    std::int32_t max_hops,
    std::int64_t model_high, std::int64_t model_low,
    HartonomousInferenceHop* path,
    std::int32_t capacity,
    std::int32_t* hop_count,
    double* total_weight)
{
    if (!path || !hop_count || !total_weight || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        mlops::MLOps ops(store);
        
        NodeRef input;
        input.id_high = input_high;
        input.id_low = input_low;
        input.is_atom = store.is_atom(input_high, input_low);
        
        NodeRef model;
        model.id_high = model_high;
        model.id_low = model_low;
        
        auto result = ops.infer(input, static_cast<std::size_t>(max_hops), model);
        
        *hop_count = static_cast<std::int32_t>(std::min(result.path.size(), 
                                                         static_cast<std::size_t>(capacity)));
        *total_weight = result.total_weight;
        
        for (std::int32_t i = 0; i < *hop_count; ++i) {
            const auto& hop = result.path[i];
            path[i].from_high = hop.from.id_high;
            path[i].from_low = hop.from.id_low;
            path[i].to_high = hop.to.id_high;
            path[i].to_low = hop.to.id_low;
            path[i].weight = hop.weight;
            path[i].rel_type = hop.rel_type;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_infer_to(
    std::int64_t input_high, std::int64_t input_low,
    std::int64_t target_high, std::int64_t target_low,
    std::int32_t max_hops,
    HartonomousInferenceHop* path,
    std::int32_t capacity,
    std::int32_t* hop_count,
    double* total_weight)
{
    if (!path || !hop_count || !total_weight || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        mlops::MLOps ops(store);
        
        NodeRef input;
        input.id_high = input_high;
        input.id_low = input_low;
        input.is_atom = store.is_atom(input_high, input_low);
        
        NodeRef target;
        target.id_high = target_high;
        target.id_low = target_low;
        target.is_atom = store.is_atom(target_high, target_low);
        
        auto result = ops.infer_to(input, target, static_cast<std::size_t>(max_hops));
        
        if (!result.success()) {
            *hop_count = 0;
            *total_weight = 0.0;
            return 1;  // No path found
        }
        
        *hop_count = static_cast<std::int32_t>(std::min(result.path.size(), 
                                                         static_cast<std::size_t>(capacity)));
        *total_weight = result.total_weight;
        
        for (std::int32_t i = 0; i < *hop_count; ++i) {
            const auto& hop = result.path[i];
            path[i].from_high = hop.from.id_high;
            path[i].from_low = hop.from.id_low;
            path[i].to_high = hop.to.id_high;
            path[i].to_low = hop.to.id_low;
            path[i].weight = hop.weight;
            path[i].rel_type = hop.rel_type;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_attend(
    std::int64_t query_high, std::int64_t query_low,
    std::int64_t context_high, std::int64_t context_low,
    HartonomousAttendedNode* attended,
    std::int32_t capacity,
    std::int32_t* count)
{
    if (!attended || !count || capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        mlops::MLOps ops(store);
        
        NodeRef query;
        query.id_high = query_high;
        query.id_low = query_low;
        query.is_atom = store.is_atom(query_high, query_low);
        
        NodeRef context;
        context.id_high = context_high;
        context.id_low = context_low;
        
        auto result = ops.attend(query, context, static_cast<std::size_t>(capacity));
        
        *count = static_cast<std::int32_t>(std::min(result.attended.size(), 
                                                     static_cast<std::size_t>(capacity)));
        for (std::int32_t i = 0; i < *count; ++i) {
            const auto& node = result.attended[i];
            attended[i].ref_high = node.ref.id_high;
            attended[i].ref_low = node.ref.id_low;
            attended[i].attention_weight = node.attention_weight;
            attended[i].raw_score = node.raw_score;
        }
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_complete(
    const char* prompt,
    std::int32_t prompt_len,
    std::int32_t max_tokens,
    double temperature,
    std::uint64_t seed,
    char* buffer,
    std::int32_t buffer_capacity,
    std::int32_t* generated_len)
{
    if (!prompt || !buffer || !generated_len || buffer_capacity <= 0) return -1;
    
    try {
        auto& store = get_store();
        mlops::MLOps ops(store);
        
        // Encode prompt
        std::string prompt_str(prompt, static_cast<std::size_t>(prompt_len));
        auto context = store.encode_and_store(prompt_str);
        
        // Generate tokens iteratively
        std::string generated;
        NodeRef current = context;
        
        for (std::int32_t i = 0; i < max_tokens && current.id_high != 0; ++i) {
            auto next = ops.generate_next(current, temperature, seed + i);
            if (next.id_high == 0 && next.id_low == 0) break;
            
            // Decode next token
            try {
                std::string token = store.decode_string(next);
                generated += token;
            } catch (...) {
                // Can't decode, stop generating
                break;
            }
            
            current = next;
        }
        
        *generated_len = static_cast<std::int32_t>(generated.size());
        if (*generated_len > buffer_capacity) {
            *generated_len = buffer_capacity;
        }
        std::memcpy(buffer, generated.data(), static_cast<std::size_t>(*generated_len));
        
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

HARTONOMOUS_API int hartonomous_ask(
    const char* question,
    std::int32_t question_len,
    std::int32_t max_hops,
    char* buffer,
    std::int32_t buffer_capacity,
    std::int32_t* answer_len,
    double* confidence)
{
    if (!question || !buffer || !answer_len || !confidence || buffer_capacity <= 0) return -1;
    (void)max_hops;  // Reserved for future multi-hop traversal
    
    try {
        auto& store = get_store();
        mlops::MLOps ops(store);
        
        // =====================================================================
        // PHASE 1: Encode query tokens
        // =====================================================================
        std::string question_str(question, static_cast<std::size_t>(question_len));
        UniversalTokenizer tokenizer;
        auto tokens = tokenizer.tokenize(
            reinterpret_cast<const std::uint8_t*>(question_str.data()), 
            question_str.size());
        
        std::vector<NodeRef> query_refs;
        query_refs.reserve(tokens.size());
        
        for (const auto& tok : tokens) {
            if (tok.length == 1 && (tok.data[0] == ' ' || tok.data[0] == '\n' || tok.data[0] == '\t')) {
                continue;
            }
            
            auto codepoints = UTF8Decoder::decode(tok.data, tok.length);
            if (codepoints.empty()) continue;
            
            NodeRef tok_ref;
            if (codepoints.size() == 1) {
                tok_ref = CodepointAtomTable::ref(codepoints[0]);
            } else {
                // Use store's encoder for consistent hashing
                tok_ref = store.compute_cpe_hash(codepoints, 0, codepoints.size());
            }
            query_refs.push_back(tok_ref);
        }
        
        if (query_refs.empty()) {
            *answer_len = 0;
            *confidence = 0.0;
            return 1;
        }
        
        // =====================================================================
        // PHASE 2: TRAJECTORY-BASED SEMANTIC QUERY
        // =====================================================================
        // Build trajectory for the query and find similar trajectories.
        // Use ST_FrechetDistance for trajectory similarity.
        // Also use relationship intersection for disambiguation.
        
        auto make_key = [](NodeRef r) {
            return static_cast<std::uint64_t>(r.id_high) ^
                   (static_cast<std::uint64_t>(r.id_low) * 0x9e3779b97f4a7c15ULL);
        };
        
        // Build query trajectory from the question text
        Trajectory query_traj = store.build_trajectory(question_str);
        
        struct ScoredAnswer {
            NodeRef ref;
            double score;
            std::size_t query_hits;
        };
        std::unordered_map<std::uint64_t, ScoredAnswer> candidates;
        
        // METHOD 1: Find COMPOSITIONS with similar trajectories using ST_FrechetDistance
        if (query_traj.expanded_length() >= 2) {
            std::string query_wkt = query_traj.to_wkt();
            
            // Query compositions whose trajectories are geometrically similar
            char sql[2048];
            std::snprintf(sql, sizeof(sql),
                "SELECT hilbert_high, hilbert_low, "
                "       ST_FrechetDistance(trajectory, ST_GeomFromText('%s')) as dist, "
                "       obs_count "
                "FROM composition "
                "WHERE trajectory IS NOT NULL "
                "ORDER BY dist "
                "LIMIT 100",
                query_wkt.c_str());
            
            PGresult* res = PQexec(store.connection(), sql);
            if (PQresultStatus(res) == PGRES_TUPLES_OK) {
                int n = PQntuples(res);
                for (int i = 0; i < n; ++i) {
                    NodeRef comp_ref;
                    comp_ref.id_high = std::stoll(PQgetvalue(res, i, 0));
                    comp_ref.id_low = std::stoll(PQgetvalue(res, i, 1));
                    comp_ref.is_atom = false;
                    double dist = std::stod(PQgetvalue(res, i, 2));
                    int obs = std::stoi(PQgetvalue(res, i, 3));
                    
                    // Score inversely proportional to Frechet distance, scaled by obs_count
                    // Lower distance = more similar trajectory = higher score
                    double score = static_cast<double>(obs) * 100.0 / (1.0 + dist);
                    
                    std::uint64_t key = make_key(comp_ref);
                    auto it = candidates.find(key);
                    if (it == candidates.end()) {
                        candidates[key] = {comp_ref, score, 1};
                    } else {
                        it->second.score += score;
                        it->second.query_hits++;
                    }
                }
            }
            PQclear(res);
        }
        
        // METHOD 2: Relationship intersection for individual query tokens
        for (const auto& query_ref : query_refs) {
            auto outgoing = store.find_from(query_ref, 50);
            auto incoming = store.find_to(query_ref, 50);
            
            for (const auto& rel : outgoing) {
                std::uint64_t key = make_key(rel.to);
                double score = static_cast<double>(rel.obs_count);
                auto it = candidates.find(key);
                if (it == candidates.end()) {
                    candidates[key] = {rel.to, score, 1};
                } else {
                    it->second.score += score;
                    it->second.query_hits++;
                }
            }
            for (const auto& rel : incoming) {
                std::uint64_t key = make_key(rel.from);
                double score = static_cast<double>(rel.obs_count);
                auto it = candidates.find(key);
                if (it == candidates.end()) {
                    candidates[key] = {rel.from, score, 1};
                } else {
                    it->second.score += score;
                    it->second.query_hits++;
                }
            }
        }
        
        // Convert to vector with intersection bonus
        std::vector<ScoredAnswer> all_answers;
        all_answers.reserve(candidates.size());
        
        for (const auto& [key, cand] : candidates) {
            // Score = query_hits^3 * total_score (strong intersection bonus)
            double intersection_bonus = static_cast<double>(cand.query_hits * cand.query_hits * cand.query_hits);
            double final_score = intersection_bonus * cand.score;
            all_answers.push_back({cand.ref, final_score, cand.query_hits});
        }
        
        // =====================================================================
        // PHASE 3: Sort by score (already aggregated above)
        // =====================================================================
        std::sort(all_answers.begin(), all_answers.end(),
            [](const auto& a, const auto& b) { return a.score > b.score; });
        
        // =====================================================================
        // PHASE 4: Decode best answer (skip single chars and query echoes)
        // =====================================================================
        std::string answer;
        double best_score = 0.0;
        
        // Build set of query token keys to avoid echoing query back
        std::unordered_set<std::uint64_t> query_keys;
        for (const auto& qr : query_refs) {
            query_keys.insert(make_key(qr));
        }
        
        for (const auto& ans : all_answers) {
            // Skip if this is one of the query tokens
            if (query_keys.count(make_key(ans.ref))) continue;
            
            try {
                std::string decoded = store.decode_string(ans.ref);
                
                // Skip empty
                if (decoded.empty()) continue;
                
                // Count actual Unicode codepoints
                size_t char_count = 0;
                for (size_t i = 0; i < decoded.size(); ) {
                    unsigned char c = decoded[i];
                    if (c < 0x80) { ++i; }
                    else if ((c & 0xE0) == 0xC0) { i += 2; }
                    else if ((c & 0xF0) == 0xE0) { i += 3; }
                    else if ((c & 0xF8) == 0xF0) { i += 4; }
                    else { ++i; }
                    ++char_count;
                }
                
                // Skip very short words (likely function words: a, an, the, is, of, to, in, etc.)
                // Prefer content words with 4+ characters
                if (char_count < 4) continue;
                
                answer = decoded;
                best_score = ans.score;
                break;
            } catch (...) {
                continue;
            }
        }
        
        if (answer.empty()) {
            *answer_len = 0;
            *confidence = 0.0;
            return 1;
        }
        
        *answer_len = static_cast<std::int32_t>(answer.size());
        if (*answer_len > buffer_capacity) {
            *answer_len = buffer_capacity;
        }
        std::memcpy(buffer, answer.data(), static_cast<std::size_t>(*answer_len));
        
        // Confidence based on aggregated score
        *confidence = std::min(1.0, best_score);
        
        return 0;
    } catch (const std::exception&) {
        return -2;
    }
}

#else // !HARTONOMOUS_HAS_DATABASE

// Stub implementations when database support not compiled
HARTONOMOUS_API int hartonomous_db_init() { return -100; }
HARTONOMOUS_API int hartonomous_db_stats(HartonomousDbStats*) { return -100; }
HARTONOMOUS_API int hartonomous_ingest(const char*, double, HartonomousIngestResult*) { return -100; }
HARTONOMOUS_API int hartonomous_content_exists(const char*, std::int32_t, int*) { return -100; }
HARTONOMOUS_API int hartonomous_encode_and_store(const char*, std::int32_t, std::int64_t*, std::int64_t*) { return -100; }
HARTONOMOUS_API int hartonomous_decode(std::int64_t, std::int64_t, char*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_find_similar(std::int32_t, HartonomousSpatialMatch*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_find_near(std::int32_t, double, HartonomousSpatialMatch*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_find_case_variants(std::int32_t, HartonomousSpatialMatch*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_find_diacritical_variants(std::int32_t, HartonomousSpatialMatch*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_store_relationship(std::int64_t, std::int64_t, std::int64_t, std::int64_t, double, std::int16_t, std::int64_t, std::int64_t) { return -100; }
HARTONOMOUS_API int hartonomous_find_from(std::int64_t, std::int64_t, HartonomousRelationship*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_find_to(std::int64_t, std::int64_t, HartonomousRelationship*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_find_by_type(std::int64_t, std::int64_t, std::int16_t, HartonomousRelationship*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_find_by_weight(double, double, std::int64_t, std::int64_t, HartonomousRelationship*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_get_weight(std::int64_t, std::int64_t, std::int64_t, std::int64_t, std::int64_t, std::int64_t, double*) { return -100; }
HARTONOMOUS_API int hartonomous_build_trajectory(const char*, std::int32_t, HartonomousTrajectoryPoint*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_store_trajectory(std::int64_t, std::int64_t, std::int64_t, std::int64_t, const HartonomousTrajectoryPoint*, std::int32_t, double, std::int16_t, std::int64_t, std::int64_t) { return -100; }
HARTONOMOUS_API int hartonomous_trajectory_to_text(const HartonomousTrajectoryPoint*, std::int32_t, char*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_contains_substring(const char*, std::int32_t, int*) { return -100; }
HARTONOMOUS_API int hartonomous_find_containing(const char*, std::int32_t, std::int64_t*, std::int32_t, std::int32_t*) { return -100; }

// MLOPS stubs
HARTONOMOUS_API int hartonomous_generate(std::int64_t, std::int64_t, std::int64_t, std::int64_t, HartonomousCandidate*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_generate_next(std::int64_t, std::int64_t, double, std::uint64_t, std::int64_t*, std::int64_t*) { return -100; }
HARTONOMOUS_API int hartonomous_infer(std::int64_t, std::int64_t, std::int32_t, std::int64_t, std::int64_t, HartonomousInferenceHop*, std::int32_t, std::int32_t*, double*) { return -100; }
HARTONOMOUS_API int hartonomous_infer_to(std::int64_t, std::int64_t, std::int64_t, std::int64_t, std::int32_t, HartonomousInferenceHop*, std::int32_t, std::int32_t*, double*) { return -100; }
HARTONOMOUS_API int hartonomous_attend(std::int64_t, std::int64_t, std::int64_t, std::int64_t, HartonomousAttendedNode*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_complete(const char*, std::int32_t, std::int32_t, double, std::uint64_t, char*, std::int32_t, std::int32_t*) { return -100; }
HARTONOMOUS_API int hartonomous_ask(const char*, std::int32_t, std::int32_t, char*, std::int32_t, std::int32_t*, double*) { return -100; }

#endif // HARTONOMOUS_HAS_DATABASE

} // extern "C"
