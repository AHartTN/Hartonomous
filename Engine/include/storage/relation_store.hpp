#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/bulk_copy.hpp>
#include <unordered_set>
#include <unordered_map>
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
    double k_factor = 32.0;
};

class RelationStore {
public:
    explicit RelationStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const RelationRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    bool use_dedup_;
    bool use_binary_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_;
};

class RelationSequenceStore {
public:
    explicit RelationSequenceStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const RelationSequenceRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    bool use_dedup_;
    bool use_binary_;
    // Dedup key: (relation_id, ordinal) — matches the unique constraint
    struct SeqKey {
        BLAKE3Pipeline::Hash relation_id;
        uint32_t ordinal;
        bool operator==(const SeqKey& o) const {
            return relation_id == o.relation_id && ordinal == o.ordinal;
        }
    };
    struct SeqKeyHasher {
        size_t operator()(const SeqKey& k) const {
            size_t h = HashHasher{}(k.relation_id);
            h ^= std::hash<uint32_t>{}(k.ordinal) + 0x9e3779b9 + (h << 6) + (h >> 2);
            return h;
        }
    };
    std::unordered_set<SeqKey, SeqKeyHasher> seen_;
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
    // Pre-aggregate ratings for same relation_id within a batch.
    // Key = relation_id, Value = accumulated record.
    std::unordered_map<BLAKE3Pipeline::Hash, RelationRatingRecord, HashHasher> pending_;
};

}
