#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/bulk_copy.hpp>
#include <unordered_set>
#include <vector>

namespace Hartonomous {

struct RelationRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash physicality_id;
};

struct RelationSequenceRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash relation_id;
    BLAKE3Pipeline::Hash composition_id;
    uint32_t ordinal;
    uint32_t occurrences = 1;
};

struct RelationRatingRecord {
    BLAKE3Pipeline::Hash relation_id;
    uint64_t observations = 1;
    double rating_value = 1000.0;
    double k_factor = 1.0;
};

class RelationStore {
public:
    // use_temp_table=true: Safe mode with ON CONFLICT DO NOTHING (slower)
    // use_temp_table=false: Direct COPY mode (fast, requires pre-deduplication)
    explicit RelationStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const RelationRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    bool use_dedup_;
    bool use_binary_;
    std::unordered_set<std::string> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
};

class RelationSequenceStore {
public:
    explicit RelationSequenceStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const RelationSequenceRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    bool use_binary_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
};

class RelationRatingStore {
public:
    explicit RelationRatingStore(PostgresConnection& db, bool use_binary = false);
    void store(const RelationRatingRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    bool use_binary_;
    std::unordered_set<std::string> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
};

}
