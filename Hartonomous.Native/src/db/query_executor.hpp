#pragma once

/// QUERY EXECUTOR - Template-based PostgreSQL query execution
///
/// Eliminates repetitive patterns:
/// - snprintf → PQexec → result parsing
/// - Error handling boilerplate
/// - Row-to-struct mapping
///
/// Usage:
///   auto results = QueryExecutor::execute<NodeRef>(conn, query,
///       [](const PgResult& res, int row) {
///           return NodeRef{ std::stoll(res.get_value(row, 0)),
///                          std::stoll(res.get_value(row, 1)) };
///       });

#include "connection.hpp"
#include "pg_result.hpp"
#include <libpq-fe.h>
#include <vector>
#include <optional>
#include <string>
#include <cstdio>
#include <cstdarg>
#include <functional>
#include <stdexcept>

namespace hartonomous::db {

/// Query result status
enum class QueryStatus {
    Ok,
    NoRows,
    Error
};

/// Query executor with templates for type-safe result mapping
class QueryExecutor {
public:
    /// Execute query and map each row to type T using mapper function.
    template<typename T, typename Mapper>
    [[nodiscard]] static std::vector<T> execute(
        PgConnection& conn,
        const char* query,
        Mapper&& mapper)
    {
        PgResult res(PQexec(conn.get(), query));
        std::vector<T> results;

        if (res.status() != PGRES_TUPLES_OK) {
            return results;  // Empty on error
        }

        results.reserve(static_cast<std::size_t>(res.row_count()));
        for (int i = 0; i < res.row_count(); ++i) {
            results.push_back(mapper(res, i));
        }

        return results;
    }

    /// Execute query and return single optional result.
    template<typename T, typename Mapper>
    [[nodiscard]] static std::optional<T> execute_single(
        PgConnection& conn,
        const char* query,
        Mapper&& mapper)
    {
        PgResult res(PQexec(conn.get(), query));

        if (res.status() != PGRES_TUPLES_OK || res.row_count() == 0) {
            return std::nullopt;
        }

        return mapper(res, 0);
    }

    /// Execute non-SELECT query (INSERT, UPDATE, DELETE).
    /// Returns number of affected rows, or -1 on error.
    [[nodiscard]] static int execute_command(
        PgConnection& conn,
        const char* query)
    {
        PgResult res(PQexec(conn.get(), query));

        if (res.status() != PGRES_COMMAND_OK) {
            return -1;
        }

        const char* affected = PQcmdTuples(res.get());
        return affected ? std::atoi(affected) : 0;
    }

    /// Execute query with printf-style formatting.
    /// Buffer size is template parameter (default 1024).
    template<std::size_t BufferSize = 1024, typename T, typename Mapper>
    [[nodiscard]] static std::vector<T> execute_fmt(
        PgConnection& conn,
        Mapper&& mapper,
        const char* fmt, ...)
    {
        char buffer[BufferSize];
        va_list args;
        va_start(args, fmt);
        std::vsnprintf(buffer, sizeof(buffer), fmt, args);
        va_end(args);

        return execute<T>(conn, buffer, std::forward<Mapper>(mapper));
    }

    /// Execute single-result query with printf-style formatting.
    template<std::size_t BufferSize = 1024, typename T, typename Mapper>
    [[nodiscard]] static std::optional<T> execute_single_fmt(
        PgConnection& conn,
        Mapper&& mapper,
        const char* fmt, ...)
    {
        char buffer[BufferSize];
        va_list args;
        va_start(args, fmt);
        std::vsnprintf(buffer, sizeof(buffer), fmt, args);
        va_end(args);

        return execute_single<T>(conn, buffer, std::forward<Mapper>(mapper));
    }

    /// Execute command with printf-style formatting.
    template<std::size_t BufferSize = 1024>
    [[nodiscard]] static int execute_command_fmt(
        PgConnection& conn,
        const char* fmt, ...)
    {
        char buffer[BufferSize];
        va_list args;
        va_start(args, fmt);
        std::vsnprintf(buffer, sizeof(buffer), fmt, args);
        va_end(args);

        return execute_command(conn, buffer);
    }

    /// Check if query succeeds (for validation/testing).
    [[nodiscard]] static bool query_ok(PgConnection& conn, const char* query) {
        PgResult res(PQexec(conn.get(), query));
        return res.status() == PGRES_TUPLES_OK || res.status() == PGRES_COMMAND_OK;
    }
};

// =============================================================================
// Common result mappers (reusable lambdas)
// =============================================================================

/// Extract int64 pair from columns 0 and 1
inline auto mapper_int64_pair = [](const PgResult& res, int row) {
    return std::pair<std::int64_t, std::int64_t>{
        std::stoll(res.get_value(row, 0)),
        std::stoll(res.get_value(row, 1))
    };
};

/// Extract double from column 0
inline auto mapper_double = [](const PgResult& res, int row) {
    return std::stod(res.get_value(row, 0));
};

/// Extract size_t from column 0
inline auto mapper_count = [](const PgResult& res, int row) {
    return std::stoull(res.get_value(row, 0));
};

/// Extract string from column 0
inline auto mapper_string = [](const PgResult& res, int row) {
    return std::string(res.get_value(row, 0));
};

} // namespace hartonomous::db
