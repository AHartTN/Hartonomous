/**
 * @file postgres_connection.hpp
 * @brief PostgreSQL connection and query interface
 */

#pragma once

#include <string>
#include <vector>
#include <memory>
#include <optional>
#include <functional>
#include <libpq-fe.h>

namespace Hartonomous {

/**
 * @brief PostgreSQL connection wrapper
 *
 * Manages connection to hypercube database with environment-based configuration.
 */
class PostgresConnection {
public:
    /**
     * @brief Connect using environment variables
     *
     * Uses: PGHOST, PGPORT, PGDATABASE, PGUSER, PGPASSWORD
     * Defaults: localhost, 5432, hypercube, postgres, (no password)
     */
    PostgresConnection();

    /**
     * @brief Connect with explicit connection string
     */
    explicit PostgresConnection(const std::string& conninfo);

    ~PostgresConnection();

    // No copy
    PostgresConnection(const PostgresConnection&) = delete;
    PostgresConnection& operator=(const PostgresConnection&) = delete;

    // Move OK
    PostgresConnection(PostgresConnection&& other) noexcept;
    PostgresConnection& operator=(PostgresConnection&& other) noexcept;

    /**
     * @brief Check if connected
     */
    bool is_connected() const;

    /**
     * @brief Execute query (no results expected)
     */
    void execute(const std::string& sql);

    /**
     * @brief Execute query with parameters (no results)
     */
    void execute(const std::string& sql, const std::vector<std::string>& params);

    /**
     * @brief Execute query and return single value
     */
    std::optional<std::string> query_single(const std::string& sql);

    /**
     * @brief Execute query and return single value with params
     */
    std::optional<std::string> query_single(const std::string& sql, const std::vector<std::string>& params);

    /**
     * @brief Execute query and iterate rows
     * @param callback Called for each row: callback(row_data)
     */
    void query(const std::string& sql, std::function<void(const std::vector<std::string>&)> callback);

    /**
     * @brief Execute query with params and iterate rows
     */
    void query(const std::string& sql, const std::vector<std::string>& params,
               std::function<void(const std::vector<std::string>&)> callback);

    /**
     * @brief Begin transaction
     */
    void begin();

    /**
     * @brief Commit transaction
     */
    void commit();

    /**
     * @brief Rollback transaction
     */
    void rollback();

    /**
     * @brief RAII transaction guard
     */
    class Transaction {
    public:
        explicit Transaction(PostgresConnection& conn);
        ~Transaction();

        void commit();
        void rollback();

    private:
        PostgresConnection& conn_;
        bool committed_ = false;
        bool rolled_back_ = false;
    };

    /**
     * @brief Get last error message
     */
    std::string last_error() const;

private:
    void connect(const std::string& conninfo);
    void disconnect();
    void check_result(PGresult* result);

    PGconn* conn_ = nullptr;
    std::string last_error_;
};

} // namespace Hartonomous
