#include <storage/atom_store.hpp>
#include <iomanip>
#include <sstream>
#include <endian.h>

namespace Hartonomous {

AtomStore::AtomStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("Atom", {"Id", "Codepoint", "PhysicalityId"});
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

    uint32_t cp_be = htobe32(rec.codepoint);
    std::ostringstream cp_hex;
    cp_hex << "\\\\x" << std::hex << std::setfill('0') << std::setw(8) << cp_be;

    copy_.add_row({uuid, cp_hex.str(), hash_to_uuid(rec.physicality_id)});
    seen_.insert(uuid);
}

void AtomStore::flush() {
    copy_.flush();
}

}
