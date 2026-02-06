#include <storage/relation_store.hpp>
#include <storage/format_utils.hpp>
#include <cstring>

namespace Hartonomous {

// ============================================================================
// RelationStore
// ============================================================================

RelationStore::RelationStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_dedup_(use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
    copy_.begin_table("hartonomous.relation", {"id", "physicalityid"});
}

void RelationStore::store(const RelationRecord& rec) {
    if (use_binary_) {
        if (use_dedup_) {
            if (seen_.count(rec.id)) return;
            seen_.insert(rec.id);
        }
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.physicality_id);
        copy_.add_row(row);
    } else {
        std::string uuid = hash_to_uuid(rec.id);
        if (use_dedup_) {
            if (seen_.count(rec.id)) return;
            seen_.insert(rec.id);
        }
        copy_.add_row({uuid, hash_to_uuid(rec.physicality_id)});
    }
}

void RelationStore::flush() {
    copy_.flush();
}

// ============================================================================
// RelationSequenceStore
// ============================================================================

RelationSequenceStore::RelationSequenceStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_dedup_(use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
    copy_.begin_table("hartonomous.relationsequence",
                      {"id", "relationid", "compositionid", "ordinal", "occurrences"});

    // Handle cross-batch duplicates: same (relation, ordinal) may appear in different batches.
    // Accumulate occurrences to preserve training signal.
    // uint32 is stored as 4-byte big-endian bytea; decode → add → re-encode.
    copy_.set_conflict_clause(
        "ON CONFLICT (relationid, ordinal) DO UPDATE SET "
        "occurrences = int4send("
            "uint32_to_int(hartonomous.relationsequence.occurrences) + "
            "uint32_to_int(EXCLUDED.occurrences)"
        "), "
        "modifiedat = CURRENT_TIMESTAMP"
    );
}

void RelationSequenceStore::store(const RelationSequenceRecord& rec) {
    if (use_dedup_) {
        SeqKey key{rec.relation_id, rec.ordinal};
        if (seen_.count(key)) return;
        seen_.insert(key);
    }
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

void RelationSequenceStore::flush() {
    copy_.flush();
}

// ============================================================================
// RelationRatingStore
// ============================================================================

RelationRatingStore::RelationRatingStore(PostgresConnection& db, bool use_binary) 
    : copy_(db, true), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
    copy_.begin_table("hartonomous.relationrating",
                      {"relationid", "observations", "ratingvalue", "kfactor"});

    // Cross-batch conflict: native C uint64_add for observations,
    // weighted_elo_update for rating (both operate on raw bytes, no casting).
    copy_.set_conflict_clause(
        "ON CONFLICT (relationid) DO UPDATE SET "
        "observations = uint64_add(hartonomous.relationrating.observations, EXCLUDED.observations), "
        "ratingvalue = weighted_elo_update("
            "hartonomous.relationrating.ratingvalue, hartonomous.relationrating.observations, "
            "EXCLUDED.ratingvalue, EXCLUDED.observations), "
        "kfactor = LEAST(hartonomous.relationrating.kfactor, EXCLUDED.kfactor), "
        "modifiedat = CURRENT_TIMESTAMP"
    );
}

void RelationRatingStore::store(const RelationRatingRecord& rec) {
    // Pre-aggregate within the batch: accumulate observations,
    // weighted-average rating_value, minimum k_factor.
    auto it = pending_.find(rec.relation_id);
    if (it != pending_.end()) {
        auto& existing = it->second;
        double total_obs = static_cast<double>(existing.observations) + static_cast<double>(rec.observations);
        existing.rating_value = (existing.rating_value * existing.observations + 
                                 rec.rating_value * rec.observations) / total_obs;
        existing.observations += rec.observations;
        existing.k_factor = std::min(existing.k_factor, rec.k_factor);
    } else {
        pending_[rec.relation_id] = rec;
    }
}

void RelationRatingStore::flush() {
    // Write pre-aggregated records to the database
    for (const auto& [_, rec] : pending_) {
        if (use_binary_) {
            BulkCopy::BinaryRow row;
            row.add_uuid(rec.relation_id);
            row.add_uint64(rec.observations);
            row.add_double(rec.rating_value);
            row.add_double(rec.k_factor);
            copy_.add_row(row);
        } else {
            copy_.add_row({
                hash_to_uuid(rec.relation_id),
                uint64_to_bytea_hex(rec.observations),
                std::to_string(rec.rating_value),
                std::to_string(rec.k_factor)
            });
        }
    }
    copy_.flush();
    pending_.clear();
}

}
