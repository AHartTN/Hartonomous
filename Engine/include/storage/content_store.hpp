#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/bulk_copy.hpp>
#include <unordered_set>
#include <cstdint>

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

class ContentStore {
public:
    explicit ContentStore(PostgresConnection& db);
    void store(const ContentRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    std::unordered_set<std::string> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
    std::string hash_to_bytea(const BLAKE3Pipeline::Hash& hash);
};

}
