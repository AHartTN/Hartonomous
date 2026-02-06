#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/bulk_copy.hpp>
#include <unordered_set>

namespace Hartonomous {

struct AtomRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash physicality_id;
    uint32_t codepoint;
};

class AtomStore {
public:
    explicit AtomStore(PostgresConnection& db);
    void store(const AtomRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    std::unordered_set<std::string> seen_;
};

}
