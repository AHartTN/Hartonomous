#pragma once

#include <storage/substrate_store.hpp>

namespace Hartonomous {

struct AtomRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash physicality_id;
    uint32_t codepoint;
};

class AtomStore : public SubstrateStore<AtomRecord> {
public:
    explicit AtomStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const AtomRecord& rec) override;
};

}
