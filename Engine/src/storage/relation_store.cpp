#include <storage/relation_store.hpp>
#include <storage/format_utils.hpp>

namespace Hartonomous {

// ============================================================================
// RelationStore
// ============================================================================

RelationStore::RelationStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : SubstrateStore(db, "hartonomous.relation", {"id", "physicalityid"}, use_temp_table, use_binary) {}

void RelationStore::store(const RelationRecord& rec) {
    if (is_duplicate(rec.id)) return;

    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.physicality_id);
        copy_.add_row(row);
    } else {
        copy_.add_row({hash_to_uuid(rec.id), hash_to_uuid(rec.physicality_id)});
    }
}

// ============================================================================
// RelationSequenceStore
// ============================================================================

RelationSequenceStore::RelationSequenceStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : SubstrateStore(db, "hartonomous.relationsequence", 
                     {"id", "relationid", "compositionid", "ordinal", "occurrences"}, 
                     use_temp_table, use_binary) {}

void RelationSequenceStore::store(const RelationSequenceRecord& rec) {
    if (is_duplicate(rec.id)) return;

    // Additional sequence-specific dedup
    SeqKey key{rec.relation_id, rec.ordinal};
    if (seen_seq_.find(key) != seen_seq_.end()) return;
    seen_seq_.insert(key);

    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.relation_id);
        row.add_uuid(rec.composition_id);
        row.add_uint32(rec.ordinal);
        row.add_uint32(rec.occurrences);
        copy_.add_row(row);
    } else {
        copy_.add_row({
            hash_to_uuid(rec.id),
            hash_to_uuid(rec.relation_id),
            hash_to_uuid(rec.composition_id),
            uint32_to_bytea_hex(rec.ordinal),
            uint32_to_bytea_hex(rec.occurrences)
        });
    }
}

// ============================================================================
// RelationRatingStore
// ============================================================================

RelationRatingStore::RelationRatingStore(PostgresConnection& db, bool use_binary)
    : SubstrateStore(db, "hartonomous.relationrating", 
                     {"relationid", "observations", "ratingvalue", "kfactor"}, 
                     true, use_binary) 
{
    // Ratings use temp table by default for ON CONFLICT aggregation
    copy_.set_conflict_clause(
        "ON CONFLICT (relationid) DO UPDATE SET "
        "observations = hartonomous.relationrating.observations + EXCLUDED.observations, "
        "ratingvalue = EXCLUDED.ratingvalue, " // Expect pre-calculated or latest
        "modifiedat = NOW()");
}

void RelationRatingStore::store(const RelationRatingRecord& rec) {
    auto it = pending_.find(rec.relation_id);
    if (it == pending_.end()) {
        pending_[rec.relation_id] = rec;
    } else {
        it->second.observations += rec.observations;
        it->second.rating_value = rec.rating_value;
    }
}

void RelationRatingStore::emit_pending() {
    for (const auto& [id, r] : pending_) {
        if (use_binary_) {
            BulkCopy::BinaryRow row;
            row.add_uuid(r.relation_id);
            row.add_uint64(r.observations);
            row.add_double(r.rating_value);
            row.add_double(r.k_factor);
            copy_.add_row(row);
        } else {
            copy_.add_row({
                hash_to_uuid(r.relation_id),
                uint64_to_bytea_hex(r.observations),
                std::to_string(r.rating_value),
                std::to_string(r.k_factor)
            });
        }
    }
    pending_.clear();
}

void RelationRatingStore::flush() {
    emit_pending();
    SubstrateStore::flush();
}

} // namespace Hartonomous