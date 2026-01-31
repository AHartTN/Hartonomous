#include <storage/relation_evidence_store.hpp>
#include <iomanip>
#include <sstream>

namespace Hartonomous {

RelationEvidenceStore::RelationEvidenceStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.relationevidence", {
        "id", "contentid", "relationid", "isvalid", "sourcerating", "signalstrength"
    });
}

std::string RelationEvidenceStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

void RelationEvidenceStore::store(const RelationEvidenceRecord& rec) {
    copy_.add_row({
        hash_to_uuid(rec.id),
        hash_to_uuid(rec.content_id),
        hash_to_uuid(rec.relation_id),
        rec.is_valid ? "true" : "false",
        std::to_string(rec.source_rating),
        std::to_string(rec.signal_strength)
    });
}

void RelationEvidenceStore::flush() {
    copy_.flush();
}

}
