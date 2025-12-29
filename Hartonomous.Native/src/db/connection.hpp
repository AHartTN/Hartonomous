#pragma once

#include <string>
#include <cstdlib>
#include <regex>
#include <libpq-fe.h>

namespace hartonomous::db {

/// Database connection configuration.
/// Uses HARTONOMOUS_DB_URL if set, otherwise falls back to local default.
struct ConnectionConfig {
    /// Default connection string for local development.
    static constexpr const char* DEFAULT_URL =
        "postgresql://hartonomous:hartonomous@localhost:5433/hartonomous";

    static std::string connection_string() {
        const char* url = std::getenv("HARTONOMOUS_DB_URL");
        if (url && url[0] != '\0') {
            return std::string(url);
        }
        return DEFAULT_URL;
    }
    
    /// Parse connection string and return connection to 'postgres' database
    /// for administrative operations like CREATE DATABASE
    static std::string admin_connection_string() {
        std::string url = connection_string();
        // Replace database name with 'postgres'
        std::regex db_regex("(postgresql://[^/]+/)([^?]+)(.*)");
        return std::regex_replace(url, db_regex, "$1postgres$3");
    }
    
    /// Extract database name from connection string
    static std::string database_name() {
        std::string url = connection_string();
        std::regex db_regex("postgresql://[^/]+/([^?]+)");
        std::smatch match;
        if (std::regex_search(url, match, db_regex)) {
            return match[1].str();
        }
        return "hartonomous";
    }
    
    /// Ensure database exists, create if not. Returns true if created.
    static bool ensure_database_exists() {
        std::string dbname = database_name();
        std::string admin_url = admin_connection_string();
        
        // Try connecting to admin database
        PGconn* admin = PQconnectdb(admin_url.c_str());
        if (PQstatus(admin) != CONNECTION_OK) {
            PQfinish(admin);
            return false; // Can't connect to postgres, user must fix manually
        }
        
        // Check if database exists
        std::string check_sql = "SELECT 1 FROM pg_database WHERE datname = '" + dbname + "'";
        PGresult* res = PQexec(admin, check_sql.c_str());
        bool exists = (PQresultStatus(res) == PGRES_TUPLES_OK && PQntuples(res) > 0);
        PQclear(res);
        
        if (!exists) {
            // Create database
            std::string create_sql = "CREATE DATABASE " + dbname;
            res = PQexec(admin, create_sql.c_str());
            bool created = (PQresultStatus(res) == PGRES_COMMAND_OK);
            PQclear(res);
            PQfinish(admin);
            
            if (created) {
                // Now connect to the new database and add PostGIS
                PGconn* newdb = PQconnectdb(connection_string().c_str());
                if (PQstatus(newdb) == CONNECTION_OK) {
                    PQexec(newdb, "CREATE EXTENSION IF NOT EXISTS postgis");
                }
                PQfinish(newdb);
                return true;
            }
            return false;
        }
        
        PQfinish(admin);
        return false; // Already exists
    }
};

} // namespace hartonomous::db
