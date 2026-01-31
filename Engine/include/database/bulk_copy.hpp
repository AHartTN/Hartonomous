#pragma once

#include <database/postgres_connection.hpp>
#include <string>
#include <sstream>

namespace Hartonomous {

class BulkCopy {
public:
    explicit BulkCopy(PostgresConnection& db);

    void begin_table(const std::string& table_name, const std::vector<std::string>& columns);
    void add_row(const std::vector<std::string>& values);
    void flush();
    size_t count() const { return row_count_; }

private:
    PostgresConnection& db_;
    std::stringstream buffer_;
    std::string schema_;       // Schema name (e.g., "hartonomous")
    std::string table_name_;   // Table name without schema (e.g., "physicality")
    std::vector<std::string> columns_;
    size_t row_count_ = 0;
    bool in_copy_ = false;

    void escape_value(const std::string& value);
    std::string quote_identifier(const std::string& id) const;
    std::string full_table_name() const;
};

}
