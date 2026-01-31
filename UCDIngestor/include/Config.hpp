// Config.hpp
#ifndef CONFIG_HPP
#define CONFIG_HPP

#include <string>
#include <stdexcept>
#include <cstdlib> // For getenv

struct DbConfig {
    std::string host;
    std::string user;
    std::string password;
    std::string dbname;
    std::string port;

    static DbConfig loadFromEnv() {
        DbConfig config;
        
        // Use standard PostgreSQL environment variables
        const char* host_env = std::getenv("PGHOST");
        const char* user_env = std::getenv("PGUSER");
        const char* password_env = std::getenv("PGPASSWORD");
        const char* dbname_env = std::getenv("PGDATABASE");
        const char* port_env = std::getenv("PGPORT");

        // Defaults or Errors? 
        // Standard libpq allows some to be missing (defaults to localhost/current user), 
        // but for this app's explicitness, we'll fetch them.
        // However, 'PGUSER' often defaults to os user, 'PGDATABASE' to user name.
        // Let's be robust but prefer the variables.

        config.host = host_env ? host_env : "localhost";
        config.port = port_env ? port_env : "5432";
        config.user = user_env ? user_env : std::getenv("USER"); 
        
        // Password might be in .pgpass, but we need it for the connection string builder
        // unless we rely purely on libpq's default handling by passing an empty string?
        // libpqxx connection string constructor handles empty params well if env vars are set.
        // Actually, if we just pass "" as the connection string to libpqxx, it AUTOMATICALLY 
        // reads all PG* environment variables. That is the *most* standard way.
        
        // So this whole Config struct is partially redundant if we just trust libpqxx,
        // but we need to pass *some* string.
        
        // We will return what we found for logging/debug, but the connection string
        // builder in `UcdIngestor.cpp` should probably just use the env vars directly
        // or we simplify `UcdIngestor::connect` to use an empty string or minimal string.
        
        // For now, let's map them explicitly to satisfy the existing `UcdIngestor` logic
        // which constructs: "host=... user=..."
        
        if (dbname_env) config.dbname = dbname_env;
        else throw std::runtime_error("PGDATABASE environment variable is not set.");

        if (password_env) config.password = password_env; 
        // if no password set, we leave it empty (might rely on trust/ident/pgpass)

        return config;
    }
};

#endif // CONFIG_HPP