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
    explicit RelationStore(PostgresConnection& db);
    void store(const RelationRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    std::unordered_set<std::string> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
};

class RelationSequenceStore {
public:
    explicit RelationSequenceStore(PostgresConnection& db);
    void store(const RelationSequenceRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
};

class RelationRatingStore {
public:
    explicit RelationRatingStore(PostgresConnection& db);
    void store(const RelationRatingRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    std::unordered_set<std::string> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
};

}
