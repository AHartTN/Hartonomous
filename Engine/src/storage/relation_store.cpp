#include <storage/relation_store.hpp>
#include <iomanip>
#include <sstream>

namespace Hartonomous {

// ============================================================================
// RelationStore
// ============================================================================

RelationStore::RelationStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.relation", {"id", "physicalityid"});
}

std::string RelationStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

void RelationStore::store(const RelationRecord& rec) {
    std::string uuid = hash_to_uuid(rec.id);
    if (seen_.count(uuid)) return;

    copy_.add_row({uuid, hash_to_uuid(rec.physicality_id)});
    seen_.insert(uuid);
}

void RelationStore::flush() {
    copy_.flush();
}

// ============================================================================
// RelationSequenceStore
// ============================================================================

RelationSequenceStore::RelationSequenceStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.relationsequence",
                      {"id", "relationid", "compositionid", "ordinal", "occurrences"});
}

std::string RelationSequenceStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

void RelationSequenceStore::store(const RelationSequenceRecord& rec) {
    copy_.add_row({
        hash_to_uuid(rec.id),
        hash_to_uuid(rec.relation_id),
        hash_to_uuid(rec.composition_id),
        std::to_string(rec.ordinal),
        std::to_string(rec.occurrences)
    });
}

void RelationSequenceStore::flush() {
    copy_.flush();
}

// ============================================================================
// RelationRatingStore
// ============================================================================

RelationRatingStore::RelationRatingStore(PostgresConnection& db) : copy_(db, true) {
    copy_.begin_table("hartonomous.relationrating",
                      {"relationid", "observations", "ratingvalue", "kfactor"});
    
    // ELO Evolution Logic:
    // 1. Increment observations
    // 2. Weighted average for ratingValue: (old * count + new) / (count + 1)
    // 3. Keep current kfactor (or update if needed)
    copy_.set_conflict_clause(
        "ON CONFLICT (relationid) DO UPDATE SET "
        "observations = hartonomous.relationrating.observations + EXCLUDED.observations, "
        "ratingvalue = (hartonomous.relationrating.ratingvalue * hartonomous.relationrating.observations + EXCLUDED.ratingvalue) / (hartonomous.relationrating.observations + 1)"
    );
}

std::string RelationRatingStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

void RelationRatingStore::store(const RelationRatingRecord& rec) {
    copy_.add_row({
        hash_to_uuid(rec.relation_id),
        std::to_string(rec.observations),
        std::to_string(rec.rating_value),
        std::to_string(rec.k_factor)
    });
}

void RelationRatingStore::flush() {
    copy_.flush();
}

}
