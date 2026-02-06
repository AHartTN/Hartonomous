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
    explicit CompositionStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const CompositionRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    bool use_dedup_;
    bool use_binary_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_;
};

class CompositionSequenceStore {
public:
    explicit CompositionSequenceStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const CompositionSequenceRecord& rec);
    void flush();
    size_t count() const { return copy_.count(); }

private:
    BulkCopy copy_;
    bool use_binary_;
};

}
