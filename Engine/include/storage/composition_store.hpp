#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/bulk_copy.hpp>
#include <unordered_set>
#include <vector>

namespace Hartonomous {

struct CompositionRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash physicality_id;
};

struct CompositionSequenceRecord {
    BLAKE3Pipeline::Hash id;
    BLAKE3Pipeline::Hash composition_id;
    BLAKE3Pipeline::Hash atom_id;
    uint32_t ordinal;
    uint32_t occurrences = 1;
};

class CompositionStore {
public:
    explicit CompositionStore(PostgresConnection& db);
    void store(const CompositionRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    std::unordered_set<std::string> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
};

class CompositionSequenceStore {
public:
    explicit CompositionSequenceStore(PostgresConnection& db);
    void store(const CompositionSequenceRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
};

}
