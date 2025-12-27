#include "exports.h"
#include "../unicode/codepoint_mapper.hpp"

#ifdef HARTONOMOUS_HAS_DATABASE
#include "../db/query_store.hpp"
#include "../db/database_store.hpp"
#include "../db/seeder.hpp"
#include "../model/model_ingest.hpp"
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
static std::unique_ptr<QueryStore> g_store;
static std::unique_ptr<DatabaseStore> g_db_store;  // For fast ingestion
static bool g_initialized = false;

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
    try {
        std::lock_guard<std::mutex> lock(g_db_mutex);
        if (!g_initialized) {
            Seeder seeder(true);  // quiet mode
            seeder.ensure_schema();
            g_store = std::make_unique<QueryStore>();
            g_initialized = true;
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
        stats->database_size_bytes = 0;  // TODO: implement
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
            // Check if model package
            bool has_tokenizer = false;
            bool has_safetensor = false;
            for (const auto& entry : std::filesystem::directory_iterator(p)) {
                if (!entry.is_regular_file()) continue;
                std::string name = entry.path().filename().string();
                if (name == "tokenizer.json" || name == "vocab.txt") has_tokenizer = true;
                if (entry.path().extension() == ".safetensors") has_safetensor = true;
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
                    store.store_model_weights(all_weights, ingester.model_context(), db::RelType::MODEL_WEIGHT);
                }
                compositions = static_cast<std::int64_t>(stored);
            } else {
                DatabaseEncoder encoder(db);
                encoder.ingest(content.data(), content.size());
                compositions = static_cast<std::int64_t>(encoder.composition_count());
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
    } catch (const std::exception&) {
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

#endif // HARTONOMOUS_HAS_DATABASE

} // extern "C"
