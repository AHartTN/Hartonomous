#include <database/bulk_copy.hpp>
#include <stdexcept>
#include <utility>
#include <cstring>
#include <arpa/inet.h> // For network byte order (htobe32, htobe64)

namespace Hartonomous {

std::atomic<uint64_t> BulkCopy::s_counter_{0};

BulkCopy::BulkCopy(PostgresConnection& db, bool use_temp_table) noexcept
    : db_(db), use_temp_table_(use_temp_table) {}

BulkCopy::~BulkCopy() {
    try {
        flush();
    } catch (...) {}
}

void BulkCopy::set_binary(bool binary) {
    if (in_copy_) throw std::runtime_error("Cannot change mode while COPY is active");
    binary_mode_ = binary;
}

// ============================================================================
// BinaryRow Implementation
// ============================================================================

void BulkCopy::BinaryRow::add_uuid(const std::array<uint8_t, 16>& uuid) {
    // 4 byte length
    int32_t len = htonl(16);
    uint8_t* p = reinterpret_cast<uint8_t*>(&len);
    buffer.insert(buffer.end(), p, p + 4);
    // 16 bytes data
    buffer.insert(buffer.end(), uuid.begin(), uuid.end());
    num_fields++;
}

void BulkCopy::BinaryRow::add_int32(int32_t val) {
    int32_t len = htonl(4);
    uint8_t* p = reinterpret_cast<uint8_t*>(&len);
    buffer.insert(buffer.end(), p, p + 4);
    
    int32_t net_val = htonl(val);
    uint8_t* v = reinterpret_cast<uint8_t*>(&net_val);
    buffer.insert(buffer.end(), v, v + 4);
    num_fields++;
}

void BulkCopy::BinaryRow::add_int64(int64_t val) {
    int32_t len = htonl(8);
    uint8_t* p = reinterpret_cast<uint8_t*>(&len);
    buffer.insert(buffer.end(), p, p + 4);

    // split 64-bit for endianness if htobe64 not available, but assuming Linux/GCC here
    // Postgres expects big-endian 64-bit
    uint64_t net_val = __builtin_bswap64(val); 
    uint8_t* v = reinterpret_cast<uint8_t*>(&net_val);
    buffer.insert(buffer.end(), v, v + 8);
    num_fields++;
}

void BulkCopy::BinaryRow::add_double(double val) {
    int32_t len = htonl(8);
    uint8_t* p = reinterpret_cast<uint8_t*>(&len);
    buffer.insert(buffer.end(), p, p + 4);

    // Standard IEEE 754 double, assume same format, just swap bytes
    uint64_t raw;
    std::memcpy(&raw, &val, 8);
    uint64_t net_val = __builtin_bswap64(raw);
    uint8_t* v = reinterpret_cast<uint8_t*>(&net_val);
    buffer.insert(buffer.end(), v, v + 8);
    num_fields++;
}

void BulkCopy::BinaryRow::add_text(const std::string& text) {
    int32_t len = htonl(static_cast<int32_t>(text.size()));
    uint8_t* p = reinterpret_cast<uint8_t*>(&len);
    buffer.insert(buffer.end(), p, p + 4);
    
    buffer.insert(buffer.end(), text.begin(), text.end());
    num_fields++;
}

void BulkCopy::BinaryRow::add_null() {
    int32_t len = htonl(-1); // -1 indicates NULL
    uint8_t* p = reinterpret_cast<uint8_t*>(&len);
    buffer.insert(buffer.end(), p, p + 4);
    num_fields++;
}

// ============================================================================
// BulkCopy Implementation
// ============================================================================

void BulkCopy::begin_table(const std::string& table_name, const std::vector<std::string>& columns) {
    // Parse optional schema.table
    auto dot_pos = table_name.find('.');
    if (dot_pos != std::string::npos) {
        schema_ = table_name.substr(0, dot_pos);
        table_name_ = table_name.substr(dot_pos + 1);
    } else {
        schema_.clear();
        table_name_ = table_name;
    }

    columns_ = columns;
    row_count_ = 0;
    
    // Reset buffers
    if (binary_mode_) {
        bin_buffer_.clear();
    } else {
        buffer_.str("");
        buffer_.clear();
    }
    
    in_copy_ = false;
    
    // Setup temp table name once
    if (use_temp_table_) {
        uint64_t id = ++s_counter_;
        temp_table_name_ = "tmp_" + table_name_ + "_" + std::to_string(id);
        temp_table_created_ = false;
    } else {
        temp_table_name_.clear();
    }
}

std::string BulkCopy::quote_identifier(const std::string& id) const {
    std::string out;
    out.reserve(id.size() + 2);
    out.push_back('"');
    for (char c : id) {
        if (c == '"') out.append("\"\"");
        else out.push_back(c);
    }
    out.push_back('"');
    return out;
}

std::string BulkCopy::full_table_name() const {
    if (schema_.empty()) {
        return quote_identifier(table_name_);
    }
    return quote_identifier(schema_) + "." + quote_identifier(table_name_);
}

void BulkCopy::escape_value_into_buffer(const std::string& value) {
    for (char c : value) {
        if (c == '\0') continue;
        switch (c) {
            case '\\': buffer_ << "\\\\"; break;
            case '\t': buffer_ << "\\t";  break;
            case '\n': buffer_ << "\\n";  break;
            case '\r': buffer_ << "\\r";  break;
            default:   buffer_ << c;      break;
        }
    }
}

void BulkCopy::write_binary_header() {
    // PGCOPY\n\377\r\n\0
    static const uint8_t header[] = {'P','G','C','O','P','Y','\n',0xFF,'\r','\n','\0'};
    bin_buffer_.insert(bin_buffer_.end(), std::begin(header), std::end(header));
    
    // Flags (0)
    int32_t flags = 0;
    uint8_t* p = reinterpret_cast<uint8_t*>(&flags);
    bin_buffer_.insert(bin_buffer_.end(), p, p + 4);
    
    // Header Ext Length (0)
    bin_buffer_.insert(bin_buffer_.end(), p, p + 4);
}

void BulkCopy::write_binary_trailer() {
    // -1 indicates end of file
    int16_t trailer = htons(-1);
    uint8_t* p = reinterpret_cast<uint8_t*>(&trailer);
    bin_buffer_.insert(bin_buffer_.end(), p, p + 2);
}

void BulkCopy::start_copy_if_needed() {
    if (in_copy_) return;

    if (columns_.empty()) {
        throw std::runtime_error("BulkCopy: columns not set. Call begin_table() first.");
    }

    std::string target_table = use_temp_table_ ? quote_identifier(temp_table_name_) : full_table_name();

    if (use_temp_table_) {
        // Ensure temp table exists
        std::ostringstream create_sql;
        create_sql << "CREATE TEMP TABLE IF NOT EXISTS " << target_table
                   << " (LIKE " << full_table_name() << " INCLUDING DEFAULTS) ON COMMIT PRESERVE ROWS";
        db_.execute(create_sql.str());
        
        std::ostringstream truncate_sql;
        truncate_sql << "TRUNCATE " << target_table;
        db_.execute(truncate_sql.str());
    }

    std::ostringstream copy_sql;
    copy_sql << "COPY " << target_table << " (";
    for (size_t i = 0; i < columns_.size(); ++i) {
        if (i) copy_sql << ", ";
        copy_sql << quote_identifier(columns_[i]);
    }
    copy_sql << ") FROM STDIN";
    
    if (binary_mode_) {
        copy_sql << " WITH (FORMAT BINARY)";
    }
    
    db_.execute(copy_sql.str());
    in_copy_ = true;

    if (binary_mode_) {
        write_binary_header();
    }
}

void BulkCopy::add_row(const std::vector<std::string>& values) {
    if (binary_mode_) throw std::runtime_error("Cannot add text row in binary mode");
    start_copy_if_needed();

    const size_t ncols = columns_.size();
    for (size_t i = 0; i < ncols; ++i) {
        if (i) buffer_ << '\t';
        if (i < values.size() && !values[i].empty() && values[i] != "\\N") {
            escape_value_into_buffer(values[i]);
        } else {
            buffer_ << "\\N";
        }
    }
    buffer_ << '\n';
    ++row_count_;

    if ((row_count_ % DEFAULT_FLUSH_ROWS) == 0) {
        std::string data = buffer_.str();
        if (!data.empty()) {
            db_.copy_data(data.c_str(), static_cast<int>(data.size()));
            buffer_.str("");
            buffer_.clear();
        }
    }
}

void BulkCopy::add_row(const BinaryRow& row) {
    if (!binary_mode_) throw std::runtime_error("Cannot add binary row in text mode");
    start_copy_if_needed();

    // 2 bytes field count
    int16_t nf = htons(row.num_fields);
    uint8_t* p = reinterpret_cast<uint8_t*>(&nf);
    bin_buffer_.insert(bin_buffer_.end(), p, p + 2);
    
    // Field data
    bin_buffer_.insert(bin_buffer_.end(), row.buffer.begin(), row.buffer.end());
    
    ++row_count_;

    if ((row_count_ % DEFAULT_FLUSH_ROWS) == 0) {
        if (!bin_buffer_.empty()) {
            db_.copy_data(reinterpret_cast<const char*>(bin_buffer_.data()), static_cast<int>(bin_buffer_.size()));
            bin_buffer_.clear();
        }
    }
}

void BulkCopy::flush() {
    if (!in_copy_) return;

    if (binary_mode_) {
        // Send remaining data + trailer
        if (!bin_buffer_.empty()) {
             db_.copy_data(reinterpret_cast<const char*>(bin_buffer_.data()), static_cast<int>(bin_buffer_.size()));
             bin_buffer_.clear();
        }
        // Construct and send trailer separately to avoid messing up buffer if it was just cleared
        std::vector<uint8_t> trailer;
        int16_t t = htons(-1);
        uint8_t* p = reinterpret_cast<uint8_t*>(&t);
        trailer.insert(trailer.end(), p, p + 2);
        db_.copy_data(reinterpret_cast<const char*>(trailer.data()), 2);
    } else {
        std::string data = buffer_.str();
        if (!data.empty()) {
            db_.copy_data(data.c_str(), static_cast<int>(data.size()));
        }
    }

    db_.copy_end(nullptr);

    if (use_temp_table_) {
        std::ostringstream sql;
        sql << "INSERT INTO " << full_table_name();
        
        std::string cols_str;
        for (size_t i = 0; i < columns_.size(); ++i) {
            if (i) cols_str += ", ";
            cols_str += quote_identifier(columns_[i]);
        }
        
        sql << " (" << cols_str << ") SELECT " << cols_str << " FROM " << quote_identifier(temp_table_name_);
        
        if (!conflict_clause_.empty()) {
            sql << " " << conflict_clause_;
        } else {
            sql << " ON CONFLICT (" << quote_identifier("id") << ") DO NOTHING";
        }
        
        db_.execute(sql.str());
    }

    in_copy_ = false;
    if (binary_mode_) bin_buffer_.clear();
    else { buffer_.str(""); buffer_.clear(); }
    row_count_ = 0;
}

void BulkCopy::set_conflict_clause(const std::string& clause) {
    conflict_clause_ = clause;
}

} // namespace Hartonomous
