#include <storage/content_store.hpp>
#include <storage/format_utils.hpp>

namespace Hartonomous {

ContentStore::ContentStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : SubstrateStore(db, "hartonomous.content", 
                     {"id", "tenantid", "userid", "contenttype", "contenthash", "contentsize", "mimetype", "language", "source", "encoding"}, 
                     use_temp_table, use_binary) {}

void ContentStore::store(const ContentRecord& rec) {
    if (is_duplicate(rec.id)) return;

    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.tenant_id);
        row.add_uuid(rec.user_id);
        row.add_uint16(rec.content_type);
        row.add_uuid(rec.content_hash);
        row.add_uint64(rec.content_size);
        row.add_text(rec.mime_type);
        row.add_text(rec.language);
        row.add_text(rec.source);
        row.add_text(rec.encoding);
        copy_.add_row(row);
    } else {
        copy_.add_row({
            hash_to_uuid(rec.id),
            hash_to_uuid(rec.tenant_id),
            hash_to_uuid(rec.user_id),
            std::to_string(rec.content_type),
            hash_to_uuid(rec.content_hash),
            std::to_string(rec.content_size),
            rec.mime_type,
            rec.language,
            rec.source,
            rec.encoding
        });
    }
}

} // namespace Hartonomous