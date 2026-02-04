#include <storage/relation_store.hpp>
#include <iomanip>
#include <sstream>
#include <cstring>

namespace Hartonomous {

// Centralized, high-performance hex conversion for UUIDs
static const char* hex_chars = "0123456789abcdef";

static std::string internal_hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    char buf[37];
    char* p = buf;
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) *p++ = '-';
        *p++ = hex_chars[(hash[i] >> 4) & 0xF];
        *p++ = hex_chars[hash[i] & 0xF];
    }
    *p = '\0';
    return std::string(buf, 36);
}

// ============================================================================
// RelationStore
// ============================================================================

RelationStore::RelationStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_dedup_(use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
    copy_.begin_table("hartonomous.relation", {"id", "physicalityid"});
}

std::string RelationStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    return internal_hash_to_uuid(hash);
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
    : copy_(db, use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
    copy_.begin_table("hartonomous.relationsequence",
                      {"id", "relationid", "compositionid", "ordinal", "occurrences"});
}

std::string RelationSequenceStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    return internal_hash_to_uuid(hash);
}

void RelationSequenceStore::store(const RelationSequenceRecord& rec) {
    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.relation_id);
        row.add_uuid(rec.composition_id);
        row.add_int32(static_cast<int32_t>(rec.ordinal));
        row.add_int32(static_cast<int32_t>(rec.occurrences));
        copy_.add_row(row);
    } else {
        copy_.add_row({
            hash_to_uuid(rec.id),
            hash_to_uuid(rec.relation_id),
            hash_to_uuid(rec.composition_id),
            std::to_string(rec.ordinal),
            std::to_string(rec.occurrences)
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
                      {"relationid", "observations", "consensuselo", "baseelo", "kfactor"});

    // Dual-ELO Evolution Logic using native weighted_elo_update
    copy_.set_conflict_clause(
        "ON CONFLICT (relationid) DO UPDATE SET "
        "baseelo = hartonomous.weighted_elo_update("
                   "hartonomous.relationrating.baseelo, hartonomous.relationrating.observations, "
                   "EXCLUDED.baseelo, EXCLUDED.observations), "
        "consensuselo = hartonomous.relationrating.consensuselo + EXCLUDED.consensuselo, "
        "observations = hartonomous.relationrating.observations + EXCLUDED.observations"
    );
}

std::string RelationRatingStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    return internal_hash_to_uuid(hash);
}

void RelationRatingStore::store(const RelationRatingRecord& rec) {
    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.relation_id);
        row.add_int64(static_cast<int64_t>(rec.observations));
        row.add_double(rec.consensus_elo);
        row.add_double(rec.base_elo);
        row.add_double(rec.k_factor);
        copy_.add_row(row);
    } else {
        copy_.add_row({
            hash_to_uuid(rec.relation_id),
            std::to_string(rec.observations),
            std::to_string(rec.consensus_elo),
            std::to_string(rec.base_elo),
            std::to_string(rec.k_factor)
        });
    }
}

void RelationRatingStore::flush() {
    copy_.flush();
}

}
