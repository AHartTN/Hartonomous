#pragma once

#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <ingestion/substrate_service.hpp>
#include <unordered_map>
#include <unordered_set>
#include <iostream>

namespace Hartonomous {

/**
 * @brief Centralized cache for substrate identities.
 * 
 * Prevents redundant compute and primary key violations during large-scale reinforcement.
 */
class SubstrateCache {
public:
    SubstrateCache() = default;

    /**
     * @brief Pre-populate the cache by streaming existing IDs from the substrate.
     */
    void pre_populate(PostgresConnection& db) {
        std::cout << "[CACHE] Pre-populating deduplication caches (streaming)..." << std::flush;
        
        db.stream_query("SELECT id FROM hartonomous.physicality", [&](const std::vector<std::string>& r) {
            phys_cache_.insert(BLAKE3Pipeline::from_hex(r[0]));
        });
        db.stream_query("SELECT id FROM hartonomous.composition", [&](const std::vector<std::string>& r) {
            comp_id_cache_.insert(BLAKE3Pipeline::from_hex(r[0]));
        });
        db.stream_query("SELECT id FROM hartonomous.relation", [&](const std::vector<std::string>& r) {
            rel_cache_.insert(BLAKE3Pipeline::from_hex(r[0]));
        });
        
        std::cout << " done (Phys: " << phys_cache_.size() 
                  << ", Comp: " << comp_id_cache_.size() 
                  << ", Rel: " << rel_cache_.size() << ")" << std::endl;
    }

    /**
     * @brief Check if a physicality ID already exists in the substrate or current session.
     */
    bool exists_phys(const BLAKE3Pipeline::Hash& id) const {
        return phys_cache_.find(id) != phys_cache_.end();
    }

    /**
     * @brief Record a new physicality ID in the cache.
     */
    void add_phys(const BLAKE3Pipeline::Hash& id) {
        phys_cache_.insert(id);
    }

    /**
     * @brief Check if a composition ID already exists.
     */
    bool exists_comp(const BLAKE3Pipeline::Hash& id) const {
        return comp_id_cache_.find(id) != comp_id_cache_.end();
    }

    /**
     * @brief Record a new composition ID.
     */
    void add_comp(const BLAKE3Pipeline::Hash& id) {
        comp_id_cache_.insert(id);
    }

    /**
     * @brief Check if a relation ID already exists.
     */
    bool exists_rel(const BLAKE3Pipeline::Hash& id) const {
        return rel_cache_.find(id) != rel_cache_.end();
    }

    /**
     * @brief Record a new relation ID.
     */
    void add_rel(const BLAKE3Pipeline::Hash& id) {
        rel_cache_.insert(id);
    }

    /**
     * @brief Map text to a cached composition.
     */
    std::optional<SubstrateService::CachedComp> get_comp(const std::string& text) const {
        auto it = comp_cache_.find(text);
        if (it != comp_cache_.end()) return it->second;
        return std::nullopt;
    }

    /**
     * @brief Cache a composition by its source text.
     */
    void cache_comp(const std::string& text, const SubstrateService::CachedComp& comp) {
        comp_cache_[text] = comp;
    }

private:
    std::unordered_map<std::string, SubstrateService::CachedComp> comp_cache_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> comp_id_cache_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_cache_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> rel_cache_;
};

} // namespace Hartonomous
