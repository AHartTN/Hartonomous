// PgConnection.hpp
#ifndef PG_CONNECTION_HPP
#define PG_CONNECTION_HPP

#include "IDatabaseConnection.hpp"
#include "Config.hpp"
#include <pqxx/pqxx> // Assuming libpqxx

class PgQueryResult : public IQueryResult {
private:
    pqxx::result m_result;
public:
    PgQueryResult(pqxx::result res) : m_result(std::move(res)) {}
    size_t size() const override { return m_result.size(); }
    const std::string& at(size_t row, size_t column) const override { 
        if (row >= m_result.size() || column >= m_result[row].size()) {
            throw std::out_of_range("Attempt to access out-of-range result column.");
        }
        return m_result[row][column].c_str(); 
    }
    const std::string& at(size_t row, const std::string& column_name) const override { 
        if (row >= m_result.size()) {
            throw std::out_of_range("Attempt to access out-of-range result row.");
        }
        return m_result[row][column_name].c_str(); 
    }
    bool is_empty() const override { return m_result.empty(); }
};

class PgConnection : public IDatabaseConnection {
private:
    std::unique_ptr<pqxx::connection> m_conn;
    std::unique_ptr<pqxx::work> m_tx; // Transaction object
public:
    void connect(const std::string& conn_str) override;
    void disconnect() override;
    void begin_transaction() override;
    void commit_transaction() override;
    void rollback_transaction() override;
    std::unique_ptr<IQueryResult> execute_query(const std::string& query) override;
    long long insert_and_get_id(const std::string& table_name, const std::map<std::string, std::string>& data) override;
    void upsert(const std::string& table_name, const std::map<std::string, std::string>& data, const std::string& conflict_target, const std::vector<std::string>& update_columns) override;
};

#endif // PG_CONNECTION_HPP
