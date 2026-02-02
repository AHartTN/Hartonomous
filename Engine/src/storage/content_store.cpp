#include <storage/content_store.hpp>
#include <iomanip>
#include <sstream>

namespace Hartonomous {

ContentStore::ContentStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.content", {
        "id", "tenantid", "userid", "contenttype", "contenthash",
        "contentsize", "contentmimetype", "contentlanguage",
        "contentsource", "contentencoding"
    });
}

std::string ContentStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

std::string ContentStore::hash_to_bytea(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (size_t i = 0; i < hash.size(); ++i) {
        ss << std::setw(2) << (static_cast<unsigned>(hash[i]) & 0xFF);
    }
    return ss.str();
}

void ContentStore::store(const ContentRecord& rec) {
    std::string uuid = hash_to_uuid(rec.id);
    if (seen_.count(uuid)) return;

    copy_.add_row({
        uuid,
        hash_to_uuid(rec.tenant_id),
        hash_to_uuid(rec.user_id),
        std::to_string(rec.content_type),
        hash_to_bytea(rec.content_hash),
        std::to_string(rec.content_size),
        rec.mime_type,
        rec.language,
        rec.source,
        rec.encoding
    });
    seen_.insert(uuid);
}

void ContentStore::flush() {
    copy_.flush();
}

}
