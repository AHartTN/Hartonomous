#pragma once

#include "pg_result.hpp"
#include "connection.hpp"
#include <vector>
#include <mutex>
#include <condition_variable>
#include <memory>

namespace hartonomous::db {

/// Thread-safe connection pool for PostgreSQL.
/// Connections are acquired and released automatically via RAII.
class ConnectionPool {
public:
    /// RAII wrapper that returns connection to pool on destruction
    class PooledConnection {
        ConnectionPool* pool_ = nullptr;
        PGconn* conn_ = nullptr;
        
    public:
        PooledConnection() = default;
        PooledConnection(ConnectionPool* pool, PGconn* conn) 
            : pool_(pool), conn_(conn) {}
        
        ~PooledConnection() { 
            if (pool_ && conn_) pool_->release(conn_); 
        }
        
        // Move-only
        PooledConnection(PooledConnection&& other) noexcept 
            : pool_(other.pool_), conn_(std::exchange(other.conn_, nullptr)) {
            other.pool_ = nullptr;
        }
        PooledConnection& operator=(PooledConnection&& other) noexcept {
            if (this != &other) {
                if (pool_ && conn_) pool_->release(conn_);
                pool_ = other.pool_;
                conn_ = std::exchange(other.conn_, nullptr);
                other.pool_ = nullptr;
            }
            return *this;
        }
        PooledConnection(const PooledConnection&) = delete;
        PooledConnection& operator=(const PooledConnection&) = delete;
        
        [[nodiscard]] PGconn* get() const noexcept { return conn_; }
        [[nodiscard]] operator PGconn*() const noexcept { return conn_; }
        [[nodiscard]] explicit operator bool() const noexcept { return conn_ != nullptr; }
        
        /// Execute a query
        [[nodiscard]] PgResult exec(const char* sql) {
            return PgResult{PQexec(conn_, sql)};
        }
        
        /// Start COPY and send data in one operation
        void copy(const char* copy_sql, const std::string& data) {
            auto res = PgResult{PQexec(conn_, copy_sql)};
            res.expect(PGRES_COPY_IN, "start_copy");
            
            if (PQputCopyData(conn_, data.c_str(), static_cast<int>(data.size())) != 1) {
                throw PgError("put_copy_data", conn_);
            }
            if (PQputCopyEnd(conn_, nullptr) != 1) {
                throw PgError("put_copy_end", conn_);
            }
            
            PgResult end_res{PQgetResult(conn_)};
            end_res.expect_ok("copy_end");
        }
    };
    
private:
    std::string connstr_;
    std::vector<PGconn*> available_;
    std::vector<PGconn*> all_connections_;
    mutable std::mutex mutex_;
    std::condition_variable cv_;
    std::size_t max_size_;
    bool shutdown_ = false;
    
public:
    /// Create a connection pool.
    /// @param size Maximum number of connections
    /// @param connstr Connection string (empty = use ConnectionConfig)
    explicit ConnectionPool(std::size_t size = 8, const std::string& connstr = "")
        : connstr_(connstr.empty() ? ConnectionConfig::connection_string() : connstr)
        , max_size_(size) {
        available_.reserve(size);
        all_connections_.reserve(size);
    }
    
    ~ConnectionPool() {
        shutdown();
    }
    
    // Non-copyable, non-movable
    ConnectionPool(const ConnectionPool&) = delete;
    ConnectionPool& operator=(const ConnectionPool&) = delete;
    ConnectionPool(ConnectionPool&&) = delete;
    ConnectionPool& operator=(ConnectionPool&&) = delete;
    
    /// Acquire a connection (blocking if none available)
    [[nodiscard]] PooledConnection acquire() {
        std::unique_lock lock(mutex_);
        
        while (available_.empty() && !shutdown_) {
            // Try to create a new connection if under limit
            if (all_connections_.size() < max_size_) {
                lock.unlock();
                PGconn* new_conn = PQconnectdb(connstr_.c_str());
                lock.lock();
                
                if (PQstatus(new_conn) == CONNECTION_OK) {
                    all_connections_.push_back(new_conn);
                    return PooledConnection{this, new_conn};
                } else {
                    PQfinish(new_conn);
                    throw PgError("Pool connection failed");
                }
            }
            
            // Wait for a connection to be released
            cv_.wait(lock);
        }
        
        if (shutdown_) {
            throw PgError("Connection pool shutdown");
        }
        
        PGconn* conn = available_.back();
        available_.pop_back();
        
        // Verify connection is still valid
        if (PQstatus(conn) != CONNECTION_OK) {
            PQreset(conn);
            if (PQstatus(conn) != CONNECTION_OK) {
                // Remove dead connection and try again
                auto it = std::find(all_connections_.begin(), all_connections_.end(), conn);
                if (it != all_connections_.end()) {
                    all_connections_.erase(it);
                }
                PQfinish(conn);
                lock.unlock();
                return acquire();
            }
        }
        
        return PooledConnection{this, conn};
    }
    
    /// Try to acquire a connection without blocking
    [[nodiscard]] PooledConnection try_acquire() {
        std::lock_guard lock(mutex_);
        
        if (!available_.empty()) {
            PGconn* conn = available_.back();
            available_.pop_back();
            return PooledConnection{this, conn};
        }
        
        if (all_connections_.size() < max_size_) {
            PGconn* new_conn = PQconnectdb(connstr_.c_str());
            if (PQstatus(new_conn) == CONNECTION_OK) {
                all_connections_.push_back(new_conn);
                return PooledConnection{this, new_conn};
            }
            PQfinish(new_conn);
        }
        
        return PooledConnection{};
    }
    
    /// Get current pool statistics
    [[nodiscard]] std::pair<std::size_t, std::size_t> stats() const {
        std::lock_guard lock(mutex_);
        return {available_.size(), all_connections_.size()};
    }
    
    /// Shutdown the pool and close all connections
    void shutdown() {
        std::lock_guard lock(mutex_);
        shutdown_ = true;
        
        for (PGconn* conn : all_connections_) {
            PQfinish(conn);
        }
        all_connections_.clear();
        available_.clear();
        
        cv_.notify_all();
    }
    
private:
    void release(PGconn* conn) {
        std::lock_guard lock(mutex_);
        if (!shutdown_) {
            available_.push_back(conn);
            cv_.notify_one();
        }
    }
};

} // namespace hartonomous::db
