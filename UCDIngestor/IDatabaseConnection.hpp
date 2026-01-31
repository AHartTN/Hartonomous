// IDatabaseConnection.hpp
#ifndef IDATABASE_CONNECTION_HPP
#define IDATABASE_CONNECTION_HPP

#include <string>
#include <vector>
#include <map>
#include <memory>

// Forward declaration for generic data row
class DataRow; // Not used directly in this interface, but conceptually could be for generic results.

// Interface for database results
class IQueryResult {
public:
    virtual ~IQueryResult() = default;
    virtual size_t size() const = 0; // Number of rows
    virtual const std::string& at(size_t row, size_t column) const = 0;
    virtual const std::string& at(size_t row, const std::string& column_name) const = 0;
    virtual bool is_empty() const = 0;
};

// Interface for database connection
class IDatabaseConnection {
public:
    virtual ~IDatabaseConnection() = default;
    virtual void connect(const std::string& conn_str) = 0;
    virtual void disconnect() = 0;
    virtual void begin_transaction() = 0;
    virtual void commit_transaction() = 0;
    virtual void rollback_transaction() = 0;
    virtual std::unique_ptr<IQueryResult> execute_query(const std::string& query) = 0;
    // Generic insert method that returns the serial ID of the newly inserted row
    virtual long long insert_and_get_id(const std::string& table_name, const std::map<std::string, std::string>& data) = 0;
    // Generic upsert method using prepared statements (for performance), requires conflict target and update columns
    virtual void upsert(const std::string& table_name, const std::map<std::string, std::string>& data, const std::string& conflict_target, const std::vector<std::string>& update_columns) = 0;
};

#endif // IDATABASE_CONNECTION_HPP
