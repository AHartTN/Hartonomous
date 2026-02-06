#include <storage/atom_store.hpp>
#include <storage/format_utils.hpp>

namespace Hartonomous {

AtomStore::AtomStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
}

void AtomStore::store(const AtomRecord& rec) {
    std::string uuid = hash_to_uuid(rec.id);
    if (seen_.count(uuid)) return;

    copy_.add_row({uuid, std::to_string(rec.codepoint), hash_to_uuid(rec.physicality_id)});
    seen_.insert(uuid);
}

void AtomStore::flush() {
    copy_.flush();
}

}
