#include <storage/atom_store.hpp>
#include <storage/format_utils.hpp>

namespace Hartonomous {

AtomStore::AtomStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : SubstrateStore(db, "hartonomous.atom", {"id", "physicalityid", "codepoint"}, use_temp_table, use_binary) {}

void AtomStore::store(const AtomRecord& rec) {
    if (is_duplicate(rec.id)) return;

    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.physicality_id);
        row.add_int32(static_cast<int32_t>(rec.codepoint));
        copy_.add_row(row);
    } else {
        copy_.add_row({
            hash_to_uuid(rec.id),
            hash_to_uuid(rec.physicality_id),
            std::to_string(rec.codepoint)
        });
    }
}

} // namespace Hartonomous