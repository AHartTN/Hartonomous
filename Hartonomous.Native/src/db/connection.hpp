#pragma once

#include <string>
#include <cstdlib>

namespace hartonomous::db {

/// Database connection configuration.
/// Uses HARTONOMOUS_DB_URL if set, otherwise falls back to local default.
struct ConnectionConfig {
    /// Default connection string for local development.
    static constexpr const char* DEFAULT_URL =
        "postgresql://hartonomous:hartonomous@localhost:5432/hartonomous";

    static std::string connection_string() {
        const char* url = std::getenv("HARTONOMOUS_DB_URL");
        if (url && url[0] != '\0') {
            return std::string(url);
        }
        return DEFAULT_URL;
    }
};

} // namespace hartonomous::db
