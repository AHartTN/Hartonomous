#pragma once

#include <storage/substrate_store.hpp>

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

class CompositionStore : public SubstrateStore<CompositionRecord> {
public:
    explicit CompositionStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const CompositionRecord& rec) override;
};

class CompositionSequenceStore : public SubstrateStore<CompositionSequenceRecord> {
public:
    explicit CompositionSequenceStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const CompositionSequenceRecord& rec) override;
};

}
