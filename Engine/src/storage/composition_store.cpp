#include <storage/composition_store.hpp>

namespace Hartonomous {

static const char* comp_hex_lut = "0123456789abcdef";

// ============================================================================
// CompositionStore
// ============================================================================

CompositionStore::CompositionStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_dedup_(use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
    copy_.begin_table("hartonomous.composition", {"id", "physicalityid"});
}

std::string CompositionStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    char buf[37];
    char* p = buf;
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) *p++ = '-';
        *p++ = comp_hex_lut[(hash[i] >> 4) & 0xF];
        *p++ = comp_hex_lut[hash[i] & 0xF];
    }
    *p = '\0';
    return std::string(buf, 36);
}

void CompositionStore::store(const CompositionRecord& rec) {
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

void CompositionStore::flush() {
    copy_.flush();
}

// ============================================================================
// CompositionSequenceStore
// ============================================================================

CompositionSequenceStore::CompositionSequenceStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
    copy_.begin_table("hartonomous.compositionsequence",
                      {"id", "compositionid", "atomid", "ordinal", "occurrences"});
}

std::string CompositionSequenceStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    char buf[37];
    char* p = buf;
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) *p++ = '-';
        *p++ = comp_hex_lut[(hash[i] >> 4) & 0xF];
        *p++ = comp_hex_lut[hash[i] & 0xF];
    }
    *p = '\0';
    return std::string(buf, 36);
}

void CompositionSequenceStore::store(const CompositionSequenceRecord& rec) {
    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.composition_id);
        row.add_uuid(rec.atom_id);
        row.add_int32(static_cast<int32_t>(rec.ordinal));
        row.add_int32(static_cast<int32_t>(rec.occurrences));
        copy_.add_row(row);
    } else {
        copy_.add_row({
            hash_to_uuid(rec.id),
            hash_to_uuid(rec.composition_id),
            hash_to_uuid(rec.atom_id),
            std::to_string(rec.ordinal),
            std::to_string(rec.occurrences)
        });
    }
}

void CompositionSequenceStore::flush() {
    copy_.flush();
}

} // namespace Hartonomous