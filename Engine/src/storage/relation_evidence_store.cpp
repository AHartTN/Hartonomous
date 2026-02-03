#include <storage/relation_evidence_store.hpp>
#include <iomanip>
#include <sstream>

namespace Hartonomous {

static const char* ev_hex_lut = "0123456789abcdef";

RelationEvidenceStore::RelationEvidenceStore(PostgresConnection& db, bool use_temp_table)
    : copy_(db, use_temp_table) {
    copy_.begin_table("hartonomous.relationevidence", {
        "id", "contentid", "relationid", "isvalid", "sourcerating", "signalstrength"
    });
}

std::string RelationEvidenceStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    char buf[37];
    char* p = buf;
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) *p++ = '-';
        *p++ = ev_hex_lut[(hash[i] >> 4) & 0xF];
        *p++ = ev_hex_lut[hash[i] & 0xF];
    }
    *p = '\0';
    return std::string(buf, 36);
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
