#pragma once

#include <storage/substrate_store.hpp>

namespace Hartonomous {

struct ContentRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash tenant_id;
    BLAKE3Pipeline::Hash user_id;
    uint16_t content_type;
    BLAKE3Pipeline::Hash content_hash;
    uint64_t content_size;
    std::string mime_type;
    std::string language;
    std::string source;
    std::string encoding;
};

class ContentStore : public SubstrateStore<ContentRecord> {
public:
    explicit ContentStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const ContentRecord& rec) override;
};

}
