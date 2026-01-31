#include "PgConnection.hpp"
#include <iostream>
#include <algorithm> // for std::min

void PgConnection::connect(const std::string& conn_str) {
    try {
        m_conn = std::make_unique<pqxx::connection>(conn_str);
        if (!m_conn->is_open()) {
            throw std::runtime_error("Failed to connect to database");
        }
    } catch (const std::exception& e) {
        std::cerr << "DB Connection Error: " << e.what() << std::endl;
        throw;
    }
}

void PgConnection::disconnect() {
    if (m_conn && m_conn->is_open()) {
        m_conn->disconnect();
    }
}

void PgConnection::begin_transaction() {
    if (!m_tx) {
        m_tx = std::make_unique<pqxx::work>(*m_conn);
    }
}

void PgConnection::commit_transaction() {
    if (m_tx) {
        m_tx->commit();
        m_tx.reset();
    }
}

void PgConnection::rollback_transaction() {
    if (m_tx) {
        m_tx->abort();
        m_tx.reset();
    }
}

std::unique_ptr<IQueryResult> PgConnection::execute_query(const std::string& query) {
    if (!m_tx) {
        pqxx::work tx(*m_conn);
        return std::make_unique<PgQueryResult>(tx.exec(query)); // Auto-commit for single query if no tx
    }
    return std::make_unique<PgQueryResult>(m_tx->exec(query));
}

long long PgConnection::insert_and_get_id(const std::string& table_name, const std::map<std::string, std::string>& data) {
    // Basic insert, returning ID
    std::string cols, vals;
    bool first = true;
    for (const auto& [k, v] : data) {
        if (!first) { cols += ","; vals += ","; }
        cols += k;
        vals += m_tx->quote(v);
        first = false;
    }
    std::string sql = "INSERT INTO " + table_name + " (" + cols + ") VALUES (" + vals + ") RETURNING id";
    pqxx::result r = m_tx->exec(sql);
    return r[0][0].as<long long>();
}

void PgConnection::upsert(const std::string& table_name, const std::map<std::string, std::string>& data, const std::string& conflict_target, const std::vector<std::string>& update_columns) {
    // ... Single row upsert implementation ...
}

void PgConnection::bulk_upsert(const std::string& table_name, const std::vector<std::map<std::string, std::string>>& data_list, const std::string& conflict_target, const std::vector<std::string>& update_columns) {
    // Multi-value INSERT
}

void PgConnection::bulk_stream_upsert(const std::string& table_name, const std::vector<std::map<std::string, std::string>>& data_list, const std::string& conflict_target, const std::vector<std::string>& update_columns) {
    if (data_list.empty()) return;
    
    // 1. Identify Columns from first row
    std::vector<std::string> columns;
    for (const auto& [k, v] : data_list[0]) columns.push_back(k);
    
    std::string cols_str;
    for (const auto& col : columns) {
        if (!cols_str.empty()) cols_str += ", ";
        cols_str += col;
    }
    
    // 2. Stream to Temp Table (or direct if no conflict)
    // If conflict_target is empty, we stream DIRECTLY to table (e.g. Staging).
    
    std::string target = table_name;
    if (!conflict_target.empty()) {
        target = "temp_" + table_name;
        std::string create_temp = "CREATE TEMP TABLE IF NOT EXISTS " + target + " (LIKE " + table_name + " INCLUDING DEFAULTS) ON COMMIT DROP";
        m_tx->exec(create_temp);
        m_tx->exec("TRUNCATE " + target);
    }
    
    // 3. Fallback to Bulk Insert (Multi-Value) to reliably compile across libpqxx versions
    // Chunked to avoid query size limits (Postgres arg limit usually ~65k params)
    size_t chunk_size = 1000;
    
    try {
        for (size_t i = 0; i < data_list.size(); i += chunk_size) {
            std::string sql = "INSERT INTO " + target + " (" + cols_str + ") VALUES ";
            bool first_row = true;
            for (size_t j = i; j < std::min(i + chunk_size, data_list.size()); ++j) {
                if (!first_row) sql += ",";
                sql += "(";
                bool first_col = true;
                for (const auto& col : columns) {
                    if (!first_col) sql += ",";
                    if (data_list[j].count(col)) sql += m_tx->quote(data_list[j].at(col));
                    else sql += "NULL";
                    first_col = false;
                }
                sql += ")";
                first_row = false;
            }
            m_tx->exec(sql);
        }
    } catch (const std::exception& e) {
        std::cerr << "Bulk Insert Error: " << e.what() << std::endl;
        throw;
    }

    // 4. Upsert from Temp if needed
    if (!conflict_target.empty()) {
        std::string update_clause;
        for (const auto& col : update_columns) {
            if (!update_clause.empty()) update_clause += ", ";
            update_clause += col + " = EXCLUDED." + col;
        }
        
        std::string sql = "INSERT INTO " + table_name + " (" + cols_str + ") SELECT " + cols_str + " FROM " + target + 
                          " ON CONFLICT (" + conflict_target + ")";
        if (!update_clause.empty()) {
            sql += " DO UPDATE SET " + update_clause;
        } else {
            sql += " DO NOTHING";
        }
        m_tx->exec(sql);
    }
}
