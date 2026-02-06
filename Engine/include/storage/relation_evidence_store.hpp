#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/bulk_copy.hpp>
#include <unordered_set>

namespace Hartonomous {

struct RelationEvidenceRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash content_id;
    BLAKE3Pipeline::Hash relation_id;
    bool is_valid = true;
    double source_rating = 1000.0;
    double signal_strength = 1.0;  // 0.0 to 1.0 based on proximity/frequency
};

class RelationEvidenceStore {
public:
    explicit RelationEvidenceStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const RelationEvidenceRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    bool use_binary_;
    bool use_dedup_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_;
};

}
