#include <storage/relation_evidence_store.hpp>
#include <storage/format_utils.hpp>

namespace Hartonomous {

RelationEvidenceStore::RelationEvidenceStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_binary_(use_binary), use_dedup_(use_temp_table) {
    copy_.set_binary(use_binary);
    copy_.set_conflict_clause(R"(ON CONFLICT ("id") DO UPDATE SET
        "sourcerating" = EXCLUDED."sourcerating",
        "signalstrength" = GREATEST(hartonomous.relationevidence."signalstrength", EXCLUDED."signalstrength"))");
    copy_.begin_table("hartonomous.relationevidence", {
        "id", "contentid", "relationid", "isvalid", "sourcerating", "signalstrength"
    });
}

void RelationEvidenceStore::store(const RelationEvidenceRecord& rec) {
    if (use_dedup_) {
        if (!seen_.insert(rec.id).second) return; // Keep first (highest signal from closer distance)
    }
    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.content_id);
        row.add_uuid(rec.relation_id);
        row.add_bool(rec.is_valid);
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
