#include <storage/composition_store.hpp>
#include <storage/format_utils.hpp>

namespace Hartonomous {

// ============================================================================
// CompositionStore
// ============================================================================

CompositionStore::CompositionStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_dedup_(use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
    copy_.begin_table("hartonomous.composition", {"id", "physicalityid"});
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
        if (use_dedup_) {
            if (seen_.count(rec.id)) return;
            seen_.insert(rec.id);
        }
        copy_.add_row({hash_to_uuid(rec.id), hash_to_uuid(rec.physicality_id)});
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

void CompositionSequenceStore::store(const CompositionSequenceRecord& rec) {
    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.composition_id);
        row.add_uuid(rec.atom_id);
        row.add_uint32(rec.ordinal);
        row.add_uint32(rec.occurrences);
        copy_.add_row(row);
    } else {
        copy_.add_row({
            hash_to_uuid(rec.id),
            hash_to_uuid(rec.composition_id),
            hash_to_uuid(rec.atom_id),
            uint32_to_bytea_hex(rec.ordinal),
            uint32_to_bytea_hex(rec.occurrences)
        });
    }
}

void CompositionSequenceStore::flush() {
    copy_.flush();
}

} // namespace Hartonomous