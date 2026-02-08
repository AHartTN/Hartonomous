/**
 * @file postgres_connection.cpp
 * @brief PostgreSQL connection implementation
 */

#include <database/postgres_connection.hpp>
#include <stdexcept>
#include <cstdlib>
#include <sstream>

namespace Hartonomous {

PostgresConnection::PostgresConnection() {
    // Build connection string from environment
    std::ostringstream conninfo;

    const char* host = std::getenv("PGHOST");
    const char* port = std::getenv("PGPORT");
    const char* dbname = std::getenv("PGDATABASE");
    const char* user = std::getenv("PGUSER");
    const char* password = std::getenv("PGPASSWORD");

    conninfo << "host=" << (host ? host : "localhost") << " ";
    conninfo << "port=" << (port ? port : "5432") << " ";
    conninfo << "dbname=" << (dbname ? dbname : "hypercube") << " ";
    conninfo << "user=" << (user ? user : "postgres") << " ";

    if (password) {
        conninfo << "password=" << password << " ";
    }

    connect(conninfo.str());
}

PostgresConnection::PostgresConnection(const std::string& conninfo) {
    connect(conninfo);
}

PostgresConnection::~PostgresConnection() {
    disconnect();
}

PostgresConnection::PostgresConnection(PostgresConnection&& other) noexcept
    : conn_(other.conn_), last_error_(std::move(other.last_error_)) {
    other.conn_ = nullptr;
}

PostgresConnection& PostgresConnection::operator=(PostgresConnection&& other) noexcept {
    if (this != &other) {
        disconnect();
        conn_ = other.conn_;
        last_error_ = std::move(other.last_error_);
        other.conn_ = nullptr;
    }
    return *this;
}

void PostgresConnection::connect(const std::string& conninfo) {
    conn_ = PQconnectdb(conninfo.c_str());

    if (PQstatus(conn_) != CONNECTION_OK) {
        last_error_ = PQerrorMessage(conn_);
        PQfinish(conn_);
        conn_ = nullptr;
        throw std::runtime_error("PostgreSQL connection failed: " + last_error_);
    }

    // Optimize for bulk loading - trade durability for speed
    PQexec(conn_, "SET synchronous_commit = off");
}

void PostgresConnection::disconnect() {
    if (conn_) {
        PQfinish(conn_);
        conn_ = nullptr;
    }
}

bool PostgresConnection::is_connected() const {
    return conn_ && PQstatus(conn_) == CONNECTION_OK;
}

void PostgresConnection::check_result(PGresult* result) {
    ExecStatusType status = PQresultStatus(result);

    if (status != PGRES_COMMAND_OK && status != PGRES_TUPLES_OK &&
        status != PGRES_COPY_IN && status != PGRES_COPY_OUT) {
        last_error_ = PQerrorMessage(conn_);
        PQclear(result);
        throw std::runtime_error("PostgreSQL query failed: " + last_error_);
    }
}

void PostgresConnection::execute(const std::string& sql) {
    if (!is_connected()) {
        throw std::runtime_error("Not connected to database");
    }

    PGresult* result = PQexec(conn_, sql.c_str());
    check_result(result);
    PQclear(result);
}

void PostgresConnection::execute(const std::string& sql, const std::vector<std::string>& params) {
    if (!is_connected()) {
        throw std::runtime_error("Not connected to database");
    }

    std::vector<const char*> param_values;
    for (const auto& p : params) {
        param_values.push_back(p.c_str());
    }

    PGresult* result = PQexecParams(
        conn_,
        sql.c_str(),
        params.size(),
        nullptr,
        param_values.data(),
        nullptr,
        nullptr,
        0  // Text format
    );

    check_result(result);
    PQclear(result);
}

std::optional<std::string> PostgresConnection::query_single(const std::string& sql) {
    if (!is_connected()) {
        throw std::runtime_error("Not connected to database");
    }

    PGresult* result = PQexec(conn_, sql.c_str());
    check_result(result);

    std::optional<std::string> value;

    if (PQntuples(result) > 0 && PQnfields(result) > 0) {
        value = PQgetvalue(result, 0, 0);
    }

    PQclear(result);
    return value;
}

std::optional<std::string> PostgresConnection::query_single(const std::string& sql, const std::vector<std::string>& params) {
    if (!is_connected()) {
        throw std::runtime_error("Not connected to database");
    }

    std::vector<const char*> param_values;
    for (const auto& p : params) {
        param_values.push_back(p.c_str());
    }

    PGresult* result = PQexecParams(
        conn_,
        sql.c_str(),
        params.size(),
        nullptr,
        param_values.data(),
        nullptr,
        nullptr,
        0
    );

    check_result(result);

    std::optional<std::string> value;

    if (PQntuples(result) > 0 && PQnfields(result) > 0) {
        value = PQgetvalue(result, 0, 0);
    }

    PQclear(result);
    return value;
}

void PostgresConnection::query(const std::string& sql, std::function<void(const std::vector<std::string>&)> callback) {
    if (!is_connected()) {
        throw std::runtime_error("Not connected to database");
    }

    PGresult* result = PQexec(conn_, sql.c_str());
    check_result(result);

    int nrows = PQntuples(result);
    int nfields = PQnfields(result);

    for (int i = 0; i < nrows; ++i) {
        std::vector<std::string> row;
        row.reserve(nfields);

        for (int j = 0; j < nfields; ++j) {
            row.push_back(PQgetvalue(result, i, j));
        }

        callback(row);
    }

    PQclear(result);
}

void PostgresConnection::query(const std::string& sql, const std::vector<std::string>& params,
                               std::function<void(const std::vector<std::string>&)> callback) {
    if (!is_connected()) {
        throw std::runtime_error("Not connected to database");
    }

    std::vector<const char*> param_values;
    for (const auto& p : params) {
        param_values.push_back(p.c_str());
    }

    PGresult* result = PQexecParams(
        conn_,
        sql.c_str(),
        params.size(),
        nullptr,
        param_values.data(),
        nullptr,
        nullptr,
        0
    );

    check_result(result);

    int nrows = PQntuples(result);
    int nfields = PQnfields(result);

    for (int i = 0; i < nrows; ++i) {
        std::vector<std::string> row;
        row.reserve(nfields);

        for (int j = 0; j < nfields; ++j) {
            row.push_back(PQgetvalue(result, i, j));
        }

        callback(row);
    }

    PQclear(result);
}

void PostgresConnection::stream_query(const std::string& sql, std::function<void(const std::vector<std::string>&)> callback) {
    if (!is_connected()) throw std::runtime_error("Not connected to database");

    if (PQsendQuery(conn_, sql.c_str()) == 0) {
        throw std::runtime_error("PQsendQuery failed: " + std::string(PQerrorMessage(conn_)));
    }

    if (PQsetSingleRowMode(conn_) == 0) {
        throw std::runtime_error("PQsetSingleRowMode failed");
    }

    PGresult* res;
    while ((res = PQgetResult(conn_)) != nullptr) {
        ExecStatusType status = PQresultStatus(res);
        
        if (status == PGRES_SINGLE_TUPLE) {
            int nfields = PQnfields(res);
            std::vector<std::string> row;
            row.reserve(nfields);
            for (int i = 0; i < nfields; ++i) {
                row.push_back(PQgetvalue(res, 0, i));
            }
            callback(row);
        } else if (status != PGRES_TUPLES_OK) {
            // PGRES_TUPLES_OK marks the end of the result set
            try {
                check_result(res);
            } catch (...) {
                PQclear(res);
                throw;
            }
        }
        PQclear(res);
    }
}

void PostgresConnection::copy_data(const char* buffer, int nbytes) {
    if (!is_connected()) {
        throw std::runtime_error("Not connected to database");
    }

    int result = PQputCopyData(conn_, buffer, nbytes);
    if (result == -1) {
        last_error_ = PQerrorMessage(conn_);
        throw std::runtime_error("COPY data failed: " + last_error_);
    }
}

void PostgresConnection::copy_end(const char* error_msg) {
    if (!is_connected()) {
        throw std::runtime_error("Not connected to database");
    }

    int result = PQputCopyEnd(conn_, error_msg);
    if (result == -1) {
        last_error_ = PQerrorMessage(conn_);
        throw std::runtime_error("COPY end failed: " + last_error_);
    }

    // After sending end, we must get the final result
    PGresult* res = PQgetResult(conn_);
    check_result(res);
    PQclear(res);
}

void PostgresConnection::begin() {
    execute("BEGIN");
}

void PostgresConnection::commit() {
    execute("COMMIT");
}

void PostgresConnection::rollback() {
    execute("ROLLBACK");
}

std::string PostgresConnection::last_error() const {
    return last_error_;
}

// Transaction RAII
PostgresConnection::Transaction::Transaction(PostgresConnection& conn) : conn_(conn) {
    conn_.begin();
}

PostgresConnection::Transaction::~Transaction() {
    if (!committed_ && !rolled_back_) {
        try {
            conn_.rollback();
        } catch (...) {
            // Ignore errors in destructor
        }
    }
}

void PostgresConnection::Transaction::commit() {
    conn_.commit();
    committed_ = true;
}

void PostgresConnection::Transaction::rollback() {
    conn_.rollback();
    rolled_back_ = true;
}

} // namespace Hartonomous
