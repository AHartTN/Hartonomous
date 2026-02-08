#pragma once

#include <database/bulk_copy.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <unordered_set>
#include <vector>
#include <string>

namespace Hartonomous {

/**
 * @brief Base class for high-performance substrate storage.
 * 
 * Provides common logic for bulk loading, deduplication, and flushing.
 */
template<typename Record>
class SubstrateStore {
public:
    SubstrateStore(PostgresConnection& db, const std::string& table_name, 
                   const std::vector<std::string>& columns,
                   bool use_temp_table = true, bool use_binary = false)
        : copy_(db, use_temp_table), 
          use_dedup_(use_temp_table), 
          use_binary_(use_binary) 
    {
        copy_.set_binary(use_binary);
        copy_.begin_table(table_name, columns);
    }

    virtual ~SubstrateStore() {
        try { flush(); } catch (...) {}
    }

    /**
     * @brief Store a record in the batch.
     */
    virtual void store(const Record& rec) = 0;

    /**
     * @brief Flush all pending records to the database.
     */
    virtual void flush() {
        copy_.flush();
    }

    /**
     * @brief Get the number of rows processed in the current session.
     */
    size_t count() const {
        return copy_.count();
    }

    /**
     * @brief Set a custom ON CONFLICT clause for the final insert.
     */
    void set_conflict_clause(const std::string& clause) {
        copy_.set_conflict_clause(clause);
    }

protected:
    /**
     * @brief Check for and record duplicates in the current session.
     */
    bool is_duplicate(const BLAKE3Pipeline::Hash& id) {
        if (!use_dedup_) return false;
        if (seen_.find(id) != seen_.end()) return true;
        seen_.insert(id);
        return false;
    }

    BulkCopy copy_;
    bool use_dedup_;
    bool use_binary_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_;
};

} // namespace Hartonomous
