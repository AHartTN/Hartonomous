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
        const char* host_env = std::getenv("UCD_DB_HOST");
        const char* user_env = std::getenv("UCD_DB_USER");
        const char* password_env = std::getenv("UCD_DB_PASSWORD");
        const char* dbname_env = std::getenv("UCD_DB_NAME");
        const char* port_env = std::getenv("UCD_DB_PORT");

        if (!host_env || !user_env || !password_env || !dbname_env || !port_env) {
            throw std::runtime_error("Missing one or more UCD_DB_ environment variables (UCD_DB_HOST, UCD_DB_USER, UCD_DB_PASSWORD, UCD_DB_NAME, UCD_DB_PORT).");
        }

        config.host = host_env;
        config.user = user_env;
        config.password = password_env;
        config.dbname = dbname_env;
        config.port = port_env;
        return config;
    }
};

#endif // CONFIG_HPP
