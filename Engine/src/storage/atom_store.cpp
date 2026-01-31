#include <storage/atom_store.hpp>
#include <iomanip>
#include <sstream>
#include <endian.h>

namespace Hartonomous {

AtomStore::AtomStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.atom", {"id", "codepoint", "physicalityid"});
}

std::string AtomStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
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
