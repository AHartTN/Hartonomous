#include <database/bulk_copy.hpp>
#include <iostream>

namespace Hartonomous {

BulkCopy::BulkCopy(PostgresConnection& db) : db_(db) {}

void BulkCopy::begin_table(const std::string& table_name, const std::vector<std::string>& columns) {
    table_name_ = table_name;
    columns_ = columns;
    row_count_ = 0;
    buffer_.str("");
    in_copy_ = false;
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
        std::ostringstream sql;
        sql << "CREATE TEMP TABLE tmp_" << table_name_
            << " (LIKE " << table_name_ << " INCLUDING DEFAULTS) ON COMMIT DROP";
        std::cerr << "DEBUG SQL: " << sql.str() << "\n";
        db_.execute(sql.str());

        sql.str("");
        sql << "COPY tmp_" << table_name_ << " (";
        for (size_t i = 0; i < columns_.size(); ++i) {
            if (i > 0) sql << ", ";
            sql << columns_[i];
        }
        sql << ") FROM STDIN";
        std::cerr << "DEBUG SQL: " << sql.str() << "\n";
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

    std::ostringstream sql;
    sql << "INSERT INTO " << table_name_ << " SELECT * FROM tmp_" << table_name_
        << " ON CONFLICT (Id) DO NOTHING";
    db_.execute(sql.str());

    in_copy_ = false;
    buffer_.str("");
}

}
