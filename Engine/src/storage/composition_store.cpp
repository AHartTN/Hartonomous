#include <storage/composition_store.hpp>
#include <iomanip>
#include <sstream>

namespace Hartonomous {

// ============================================================================
// CompositionStore
// ============================================================================

CompositionStore::CompositionStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.composition", {"id", "physicalityid"});
}

std::string CompositionStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

void CompositionStore::store(const CompositionRecord& rec) {
    std::string uuid = hash_to_uuid(rec.id);
    if (seen_.count(uuid)) return;

    copy_.add_row({uuid, hash_to_uuid(rec.physicality_id)});
    seen_.insert(uuid);
}

void CompositionStore::flush() {
    copy_.flush();
}

// ============================================================================
// CompositionSequenceStore
// ============================================================================

CompositionSequenceStore::CompositionSequenceStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.compositionsequence",
                      {"id", "compositionid", "atomid", "ordinal", "occurrences"});
}

std::string CompositionSequenceStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

void CompositionSequenceStore::store(const CompositionSequenceRecord& rec) {
    copy_.add_row({
        hash_to_uuid(rec.id),
        hash_to_uuid(rec.composition_id),
        hash_to_uuid(rec.atom_id),
        std::to_string(rec.ordinal),
        std::to_string(rec.occurrences)
    });
}

void CompositionSequenceStore::flush() {
    copy_.flush();
}

}
