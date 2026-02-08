#pragma once

#include <storage/substrate_store.hpp>

namespace Hartonomous {

struct RelationEvidenceRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash content_id;
    BLAKE3Pipeline::Hash relation_id;
    bool is_valid = true;
    double source_rating = 1000.0;
    double signal_strength = 1.0;  
};

class RelationEvidenceStore : public SubstrateStore<RelationEvidenceRecord> {
public:
    explicit RelationEvidenceStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const RelationEvidenceRecord& rec) override;
};

}
