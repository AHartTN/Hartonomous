#include <storage/relation_evidence_store.hpp>
#include <iomanip>
#include <sstream>

namespace Hartonomous {

static const char* ev_hex_lut = "0123456789abcdef";

RelationEvidenceStore::RelationEvidenceStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
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
    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.content_id);
        row.add_uuid(rec.relation_id);
        // isvalid is boolean. Postgres binary for bool is 1 byte (0/1)
        uint8_t b = rec.is_valid ? 1 : 0;
        row.add_bytes(&b, 1);
        row.add_double(rec.source_rating);
        row.add_double(rec.signal_strength);
        copy_.add_row(row);
    } else {
        copy_.add_row({
            hash_to_uuid(rec.id),
            hash_to_uuid(rec.content_id),
            hash_to_uuid(rec.relation_id),
            rec.is_valid ? "true" : "false",
            std::to_string(rec.source_rating),
            std::to_string(rec.signal_strength)
        });
    }
}

void RelationEvidenceStore::flush() {
    copy_.flush();
}

}
