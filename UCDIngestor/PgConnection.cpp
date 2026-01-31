// PgConnection.cpp
#include "PgConnection.hpp"
#include <sstream>
#include <iostream> // For error reporting
#include <stdexcept> // For std::runtime_error

void PgConnection::connect(const std::string& conn_str) {
    try {
        m_conn = std::make_unique<pqxx::connection>(conn_str);
        if (!m_conn->is_open()) {
            throw std::runtime_error("Could not open database connection.");
        }
        std::cout << "Connected to PostgreSQL database." << std::endl;
    } catch (const pqxx::sql_error& e) {
        throw std::runtime_error("SQL error during connection: " + std::string(e.what()) + " Query: " + e.query());
    } catch (const std::exception& e) {
        throw std::runtime_error("Error during connection: " + std::string(e.what()));
    }
}

void PgConnection::disconnect() {
    if (m_conn && m_conn->is_open()) {
        m_conn->disconnect();
        std::cout << "Disconnected from PostgreSQL database." << std::endl;
    }
}

void PgConnection::begin_transaction() {
    if (!m_conn || !m_conn->is_open()) {
        throw std::runtime_error("Database not connected.");
    }
    if (m_tx) {
        throw std::runtime_error("Transaction already in progress.");
    }
    m_tx = std::make_unique<pqxx::work>(*m_conn);
}

void PgConnection::commit_transaction() {
    if (!m_tx) {
        throw std::runtime_error("No active transaction to commit.");
    }
    m_tx->commit();
    m_tx.reset();
}

void PgConnection::rollback_transaction() {
    if (!m_tx) {
        throw std::runtime_error("No active transaction to rollback.");
    }
    m_tx->abort();
    m_tx.reset();
}

std::unique_ptr<IQueryResult> PgConnection::execute_query(const std::string& query) {
    if (!m_conn || !m_conn->is_open()) {
        throw std::runtime_error("Database not connected.");
    }
    try {
        pqxx::result r;
        if (m_tx) {
            r = m_tx->exec(query);
        } else {
            pqxx::nontransaction N(*m_conn);
            r = N.exec(query);
        }
        return std::make_unique<PgQueryResult>(std::move(r));
    } catch (const pqxx::sql_error& e) {
        throw std::runtime_error("SQL error during query: " + std::string(e.what()) + " Query: " + e.query());
    } catch (const std::exception& e) {
        throw std::runtime_error("Error during query: " + std::string(e.what()));
    }
}

long long PgConnection::insert_and_get_id(const std::string& table_name, const std::map<std::string, std::string>& data) {
    if (!m_tx) {
        throw std::runtime_error("Insert requires an active transaction.");
    }

    std::ostringstream cols_ss;
    std::ostringstream vals_ss;
    std::vector<std::string> param_names;
    int param_idx = 1;

    for (const auto& pair : data) {
        if (!cols_ss.str().empty()) {
            cols_ss << ", ";
            vals_ss << ", ";
        }
        cols_ss << m_tx->quote_name(pair.first);
        vals_ss << "$" << param_idx++;
        param_names.push_back(pair.first);
    }

    std::string query_str = "INSERT INTO " + m_tx->quote_name(table_name) + " (" + cols_ss.str() + ") VALUES (" + vals_ss.str() + ") RETURNING id;";
    
    // For single inserts within a transaction, it's simpler to use `exec_params` directly.
    std::vector<std::string> param_values;
    for (const auto& col_name : param_names) {
        param_values.push_back(data.at(col_name));
    }

    pqxx::result r = m_tx->exec_params(query_str, param_values);
    
    if (r.empty() || r[0].empty() || r[0][0].is_null()) {
        throw std::runtime_error("Failed to retrieve ID after insert into " + table_name);
    }
    return r[0][0].as<long long>();
}

void PgConnection::upsert(const std::string& table_name, const std::map<std::string, std::string>& data, const std::string& conflict_target, const std::vector<std::string>& update_columns) {
    if (!m_tx) {
        throw std::runtime_error("Upsert requires an active transaction.");
    }

    std::ostringstream cols_ss;
    std::ostringstream vals_ss;
    std::vector<std::string> param_names;
    std::vector<std::string> param_values;
    int param_idx = 1;

    for (const auto& pair : data) {
        if (!cols_ss.str().empty()) {
            cols_ss << ", ";
            vals_ss << ", ";
        }
        cols_ss << m_tx->quote_name(pair.first);
        vals_ss << "$" << param_idx++;
        param_names.push_back(pair.first);
        param_values.push_back(pair.second);
    }

    std::string query_str = "INSERT INTO " + m_tx->quote_name(table_name) + " (" + cols_ss.str() + ") VALUES (" + vals_ss.str() + ") ON CONFLICT ON CONSTRAINT " + m_tx->quote_name(conflict_target) + " DO UPDATE SET ";

    bool first_update_col = true;
    for (const std::string& col : update_columns) {
        if (!first_update_col) {
            query_str += ", ";
        }
        query_str += m_tx->quote_name(col) + " = EXCLUDED." + m_tx->quote_name(col);
        first_update_col = false;
    }
    query_str += ";";

    m_tx->exec_params(query_str, param_values);
}
