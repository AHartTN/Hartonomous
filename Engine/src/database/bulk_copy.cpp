#include <database/bulk_copy.hpp>
#include <iostream>

namespace Hartonomous {

BulkCopy::BulkCopy(PostgresConnection& db) : db_(db) {}

void BulkCopy::begin_table(const std::string& table_name, const std::vector<std::string>& columns) {
    // Parse schema-qualified name (schema.table) if present
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
    in_copy_ = false;
}

std::string BulkCopy::quote_identifier(const std::string& id) const {
    // Quote an identifier to preserve case and handle special characters
    std::string result = "\"";
    for (char c : id) {
        if (c == '"') result += "\"\"";  // Escape double quotes
        else result += c;
    }
    result += "\"";
    return result;
}

std::string BulkCopy::full_table_name() const {
    // Return properly quoted schema.table reference
    if (schema_.empty()) {
        return quote_identifier(table_name_);
    }
    return quote_identifier(schema_) + "." + quote_identifier(table_name_);
}

void BulkCopy::escape_value(const std::string& value) {
    for (char c : value) {
        if (c == '\\') buffer_ << "\\\\";
        else if (c == '\t') buffer_ << "\\t";
        else if (c == '\n') buffer_ << "\\n";
        else if (c == '\r') buffer_ << "\\r";
        else buffer_ << c;
    }
}

void BulkCopy::add_row(const std::vector<std::string>& values) {
    if (!in_copy_) {
        // Temp table name (no schema needed - temp tables are in pg_temp)
        std::string temp_table = quote_identifier("tmp_" + table_name_);

        std::ostringstream sql;
        sql << "CREATE TEMP TABLE " << temp_table
            << " (LIKE " << full_table_name() << " INCLUDING DEFAULTS) ON COMMIT DROP";
        db_.execute(sql.str());

        sql.str("");
        sql << "COPY " << temp_table << " (";
        for (size_t i = 0; i < columns_.size(); ++i) {
            if (i > 0) sql << ", ";
            sql << quote_identifier(columns_[i]);
        }
        sql << ") FROM STDIN";
        db_.execute(sql.str());
        in_copy_ = true;
    }

    for (size_t i = 0; i < values.size(); ++i) {
        if (i > 0) buffer_ << '\t';
        escape_value(values[i]);
    }
    buffer_ << '\n';
    row_count_++;

    if (row_count_ % 10000 == 0) {
        std::string data = buffer_.str();
        db_.copy_data(data.c_str(), data.size());
        buffer_.str("");
    }
}

void BulkCopy::flush() {
    if (!in_copy_) return;

    std::string data = buffer_.str();
    if (!data.empty()) {
        db_.copy_data(data.c_str(), data.size());
    }
    db_.copy_end();

    std::string temp_table = quote_identifier("tmp_" + table_name_);

    std::ostringstream sql;
    sql << "INSERT INTO " << full_table_name() << " SELECT * FROM " << temp_table
        << " ON CONFLICT (" << quote_identifier("Id") << ") DO NOTHING";
    db_.execute(sql.str());

    in_copy_ = false;
    buffer_.str("");
}

}