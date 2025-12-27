#pragma once

#include "connection.hpp"
#include <libpq-fe.h>
#include <string>
#include <stdexcept>
#include <utility>

namespace hartonomous::db {

/// Exception for PostgreSQL errors with context
class PgError : public std::runtime_error {
public:
    explicit PgError(const std::string& message) 
        : std::runtime_error(message) {}
    
    PgError(const char* operation, PGconn* conn)
        : std::runtime_error(std::string(operation) + ": " + PQerrorMessage(conn)) {}
    
    PgError(const char* operation, PGresult* res)
        : std::runtime_error(std::string(operation) + ": " + PQresultErrorMessage(res)) {}
};

/// RAII wrapper for PGresult*
/// Automatically clears result on destruction.
class PgResult {
    PGresult* res_ = nullptr;
    
public:
    PgResult() = default;
    explicit PgResult(PGresult* res) noexcept : res_(res) {}
    
    ~PgResult() { if (res_) PQclear(res_); }
    
    // Move-only
    PgResult(PgResult&& other) noexcept : res_(std::exchange(other.res_, nullptr)) {}
    PgResult& operator=(PgResult&& other) noexcept {
        if (this != &other) {
            if (res_) PQclear(res_);
            res_ = std::exchange(other.res_, nullptr);
        }
        return *this;
    }
    PgResult(const PgResult&) = delete;
    PgResult& operator=(const PgResult&) = delete;
    
    /// Get underlying pointer
    [[nodiscard]] PGresult* get() const noexcept { return res_; }
    [[nodiscard]] operator PGresult*() const noexcept { return res_; }
    [[nodiscard]] explicit operator bool() const noexcept { return res_ != nullptr; }
    
    /// Release ownership (caller must PQclear)
    [[nodiscard]] PGresult* release() noexcept { return std::exchange(res_, nullptr); }
    
    /// Get result status
    [[nodiscard]] ExecStatusType status() const noexcept {
        return res_ ? PQresultStatus(res_) : PGRES_FATAL_ERROR;
    }
    
    /// Check if command succeeded
    [[nodiscard]] bool ok() const noexcept {
        return status() == PGRES_COMMAND_OK || status() == PGRES_TUPLES_OK;
    }
    
    /// Check for specific status
    [[nodiscard]] bool is(ExecStatusType expected) const noexcept {
        return status() == expected;
    }
    
    /// Get error message (empty if no error)
    [[nodiscard]] std::string error_message() const {
        return res_ ? PQresultErrorMessage(res_) : "null result";
    }
    
    /// Throw if status doesn't match expected
    void expect(ExecStatusType expected, const char* operation) const {
        if (status() != expected) {
            throw PgError(operation, res_);
        }
    }
    
    /// Throw if not command_ok or tuples_ok
    void expect_ok(const char* operation) const {
        if (!ok()) {
            throw PgError(operation, res_);
        }
    }
    
    /// Get number of rows affected by INSERT/UPDATE/DELETE
    [[nodiscard]] std::size_t affected_rows() const {
        if (!res_) return 0;
        const char* affected = PQcmdTuples(res_);
        return (affected && *affected) ? std::stoull(affected) : 0;
    }
    
    /// Get number of result rows
    [[nodiscard]] int row_count() const noexcept {
        return res_ ? PQntuples(res_) : 0;
    }
    
    /// Get value at row/column
    [[nodiscard]] const char* get_value(int row, int col) const noexcept {
        return res_ ? PQgetvalue(res_, row, col) : "";
    }
};

/// RAII wrapper for PGconn*
/// Automatically closes connection on destruction.
/// Auto-bootstraps database if it doesn't exist.
class PgConnection {
    PGconn* conn_ = nullptr;
    
public:
    PgConnection() = default;
    
    explicit PgConnection(const std::string& connstr) {
        conn_ = PQconnectdb(connstr.c_str());
        if (PQstatus(conn_) != CONNECTION_OK) {
            std::string err = PQerrorMessage(conn_);
            
            // Check if database doesn't exist - auto-bootstrap
            if (err.find("does not exist") != std::string::npos) {
                PQfinish(conn_);
                conn_ = nullptr;
                
                // Try to create the database
                ConnectionConfig::ensure_database_exists();
                
                // Retry connection
                conn_ = PQconnectdb(connstr.c_str());
                if (PQstatus(conn_) == CONNECTION_OK) {
                    return; // Success after bootstrap
                }
                err = PQerrorMessage(conn_);
            }
            
            PQfinish(conn_);
            conn_ = nullptr;
            throw PgError("Connection failed: " + err);
        }
    }
    
    explicit PgConnection(PGconn* conn) noexcept : conn_(conn) {}
    
    ~PgConnection() { if (conn_) PQfinish(conn_); }
    
    // Move-only
    PgConnection(PgConnection&& other) noexcept : conn_(std::exchange(other.conn_, nullptr)) {}
    PgConnection& operator=(PgConnection&& other) noexcept {
        if (this != &other) {
            if (conn_) PQfinish(conn_);
            conn_ = std::exchange(other.conn_, nullptr);
        }
        return *this;
    }
    PgConnection(const PgConnection&) = delete;
    PgConnection& operator=(const PgConnection&) = delete;
    
    /// Get underlying pointer
    [[nodiscard]] PGconn* get() const noexcept { return conn_; }
    [[nodiscard]] operator PGconn*() const noexcept { return conn_; }
    [[nodiscard]] explicit operator bool() const noexcept { return conn_ != nullptr; }
    
    /// Check if connected
    [[nodiscard]] bool connected() const noexcept {
        return conn_ && PQstatus(conn_) == CONNECTION_OK;
    }
    
    /// Execute a query
    [[nodiscard]] PgResult exec(const char* sql) {
        return PgResult{PQexec(conn_, sql)};
    }
    
    /// Execute and expect success
    void exec_ok(const char* sql, const char* operation = "exec") {
        exec(sql).expect_ok(operation);
    }
    
    /// Start COPY FROM STDIN
    void start_copy(const char* sql) {
        auto res = exec(sql);
        res.expect(PGRES_COPY_IN, "start_copy");
    }
    
    /// Send COPY data
    void put_copy_data(const char* data, int len) {
        if (PQputCopyData(conn_, data, len) != 1) {
            throw PgError("put_copy_data", conn_);
        }
    }
    
    void put_copy_data(const std::string& data) {
        put_copy_data(data.c_str(), static_cast<int>(data.size()));
    }
    
    /// End COPY operation
    PgResult end_copy() {
        if (PQputCopyEnd(conn_, nullptr) != 1) {
            throw PgError("put_copy_end", conn_);
        }
        return PgResult{PQgetResult(conn_)};
    }
    
    /// Get last error message
    [[nodiscard]] std::string error_message() const {
        return conn_ ? PQerrorMessage(conn_) : "null connection";
    }
};

} // namespace hartonomous::db
