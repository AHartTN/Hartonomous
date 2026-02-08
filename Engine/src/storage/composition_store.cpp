#include <storage/composition_store.hpp>
#include <storage/format_utils.hpp>

namespace Hartonomous {

// ============================================================================
// CompositionStore
// ============================================================================

CompositionStore::CompositionStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : SubstrateStore(db, "hartonomous.composition", {"id", "physicalityid"}, use_temp_table, use_binary) {}

void CompositionStore::store(const CompositionRecord& rec) {
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
// CompositionSequenceStore
// ============================================================================

CompositionSequenceStore::CompositionSequenceStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : SubstrateStore(db, "hartonomous.compositionsequence",
                     {"id", "compositionid", "atomid", "ordinal", "occurrences"},
                     use_temp_table, use_binary) {}

void CompositionSequenceStore::store(const CompositionSequenceRecord& rec) {
    // CompositionSequence is unique by (compositionid, ordinal), but usually
    // callers handle sequence uniqueness. We'll use the record ID for session dedup.
    if (is_duplicate(rec.id)) return;

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

} // namespace Hartonomous
