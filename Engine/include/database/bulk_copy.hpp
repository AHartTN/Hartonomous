#pragma once

#include <database/postgres_connection.hpp>
#include <atomic>
#include <sstream>
#include <string>
#include <vector>

namespace Hartonomous {

/**
 * @brief Simple helper to stream many rows into Postgres using COPY.
 *
 * Usage:
 *   BulkCopy bc(conn);
 *   bc.begin_table("schema.table" or "table", {"col1","col2",...});
 *   for (...) bc.add_row({...});
 *   bc.flush(); // or rely on destructor to flush as a best-effort
 *
 * Notes:
 * - begin_table must be called before add_row.
 * - flush() finishes the COPY and inserts the data.
 * - This class is not thread-safe; use one instance per connection/thread.
 *
 * Modes:
 * - Default (use_temp_table=true): Creates temp table, COPYs into it, then
 *   INSERTs with ON CONFLICT DO NOTHING. Slower but handles duplicates.
 * - Direct (use_temp_table=false): COPYs directly into target table.
 *   Much faster but fails on duplicate keys.
 */
class BulkCopy {
public:
    explicit BulkCopy(PostgresConnection& db, bool use_temp_table = true) noexcept;
    ~BulkCopy();

    // Prepare for a target table and column list. Call once before add_row.
    void begin_table(const std::string& table_name, const std::vector<std::string>& columns);

    // Add a row. values.size() may be <= columns.size(); missing values become empty fields.
    void add_row(const std::vector<std::string>& values);

    // Flush remaining rows, finish COPY, and insert into the target table.
    void flush();

    // Set custom ON CONFLICT clause for temp table mode
    void set_conflict_clause(const std::string& clause);

    // Enable Postgres COPY BINARY format for faster ingestion.
    // This transmits data as raw bytes (e.g. 16-byte UUIDs) instead of text,
    // reducing CPU overhead for formatting/parsing on both ends.
    // Queryability is NOT affected; data is stored as standard types.
    void set_binary(bool binary);

    // Row builder for binary COPY
    struct BinaryRow {
        std::vector<uint8_t> buffer;
        int16_t num_fields = 0;

        void add_uuid(const std::array<uint8_t, 16>& uuid); // 16 bytes raw
        void add_int32(int32_t val);
        void add_int64(int64_t val);
        void add_double(double val);
        void add_text(const std::string& text);
        void add_null();
        
        void clear() { buffer.clear(); num_fields = 0; }
    };

    void add_row(const BinaryRow& row);

    // Number of rows added since begin_table (resets after flush)
    size_t count() const noexcept { return row_count_; }

private:
    // Helpers
    void start_copy_if_needed();
    
    // Binary COPY helpers
    void write_binary_header();
    void write_binary_trailer();

    void escape_value_into_buffer(const std::string& value);
    std::string quote_identifier(const std::string& id) const;
    std::string full_table_name() const;

    PostgresConnection& db_;
    std::ostringstream buffer_;         // For TEXT mode
    std::vector<uint8_t> bin_buffer_;   // For BINARY mode
    
    bool binary_mode_ = false;

    std::string schema_;
    std::string table_name_;
    std::vector<std::string> columns_;
    std::string temp_table_name_;
    size_t row_count_ = 0;
    bool in_copy_ = false;
    bool use_temp_table_ = true;
    bool temp_table_created_ = false;
    std::string conflict_clause_;

    static std::atomic<uint64_t> s_counter_;
    static constexpr size_t DEFAULT_FLUSH_ROWS = 50000;
};

} // namespace Hartonomous
