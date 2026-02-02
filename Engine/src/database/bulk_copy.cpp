#include <database/bulk_copy.hpp>
#include <stdexcept>
#include <utility>

namespace Hartonomous {

std::atomic<uint64_t> BulkCopy::s_counter_{0};

BulkCopy::BulkCopy(PostgresConnection& db, bool use_temp_table) noexcept
    : db_(db), use_temp_table_(use_temp_table) {}

BulkCopy::~BulkCopy() {
    // Best-effort flush; destructor must not throw
    try {
        flush();
    } catch (...) {
        // swallow exceptions in destructor
    }
}

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
    buffer_.str("");
    buffer_.clear();
    in_copy_ = false;
    temp_table_name_.clear();
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
        if (c == '\0') continue; // Postgres TEXT/VARCHAR cannot contain null bytes
        switch (c) {
            case '\\': buffer_ << "\\\\"; break;
            case '\t': buffer_ << "\\t";  break;
            case '\n': buffer_ << "\\n";  break;
            case '\r': buffer_ << "\\r";  break;
            default:   buffer_ << c;      break;
        }
    }
}

void BulkCopy::start_copy_if_needed() {
    if (in_copy_) return;

    if (columns_.empty()) {
        throw std::runtime_error("BulkCopy: columns not set. Call begin_table() first.");
    }

    if (use_temp_table_) {
        // Temp table mode: create temp table, copy into it
        uint64_t id = ++s_counter_;
        temp_table_name_ = "tmp_" + table_name_ + "_" + std::to_string(id);

        std::string quoted_temp = quote_identifier(temp_table_name_);

        // Drop any stale temp table, then create fresh
        {
            std::ostringstream drop_sql;
            drop_sql << "DROP TABLE IF EXISTS " << quoted_temp;
            db_.execute(drop_sql.str());

            std::ostringstream sql;
            sql << "CREATE TEMP TABLE " << quoted_temp
                << " (LIKE " << full_table_name() << " INCLUDING DEFAULTS)";
            db_.execute(sql.str());
        }

        // Start COPY into the temp table
        {
            std::ostringstream copy_sql;
            copy_sql << "COPY " << quoted_temp << " (";
            for (size_t i = 0; i < columns_.size(); ++i) {
                if (i) copy_sql << ", ";
                copy_sql << quote_identifier(columns_[i]);
            }
            copy_sql << ") FROM STDIN";
            db_.execute(copy_sql.str());
        }
    } else {
        // Direct mode: COPY directly into target table (faster, no conflict handling)
        std::ostringstream copy_sql;
        copy_sql << "COPY " << full_table_name() << " (";
        for (size_t i = 0; i < columns_.size(); ++i) {
            if (i) copy_sql << ", ";
            copy_sql << quote_identifier(columns_[i]);
        }
        copy_sql << ") FROM STDIN";
        db_.execute(copy_sql.str());
    }

    in_copy_ = true;
}

void BulkCopy::add_row(const std::vector<std::string>& values) {
    // Ensure COPY is started exactly once per session
    start_copy_if_needed();

    // Emit exactly columns_.size() fields; empty strings or "\N" become NULL (\N)
    const size_t ncols = columns_.size();
    for (size_t i = 0; i < ncols; ++i) {
        if (i) buffer_ << '\t';
        if (i < values.size() && !values[i].empty() && values[i] != "\\N") {
            escape_value_into_buffer(values[i]);
        } else {
            // Write PostgreSQL NULL sentinel
            buffer_ << "\\N";
        }
    }
    buffer_ << '\n';
    ++row_count_;

    // Periodic flush
    if ((row_count_ % DEFAULT_FLUSH_ROWS) == 0) {
        std::string data = buffer_.str();
        if (!data.empty()) {
            db_.copy_data(data.c_str(), static_cast<int>(data.size()));
            buffer_.str("");
            buffer_.clear();
        }
    }
}

void BulkCopy::flush() {
    if (!in_copy_) return;

    // Send remaining buffered rows
    std::string data = buffer_.str();
    if (!data.empty()) {
        db_.copy_data(data.c_str(), static_cast<int>(data.size()));
    }

    // End COPY (driver will check server response)
    db_.copy_end(nullptr);

    if (use_temp_table_) {
        // Move data from temp table into the real table
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
    // In direct mode, data is already in the target table

    // Reset state for reuse
    in_copy_ = false;
    temp_table_name_.clear();
    buffer_.str("");
    buffer_.clear();
    row_count_ = 0;
}

void BulkCopy::set_conflict_clause(const std::string& clause) {
    conflict_clause_ = clause;
}

} // namespace Hartonomous
