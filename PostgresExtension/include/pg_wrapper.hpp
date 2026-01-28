/**
 * @file pg_wrapper.hpp
 * @brief PostgreSQL C API wrapper - Thin bridge between PostgreSQL and C++ Engine
 */

#pragma once

// Windows compatibility: Include winsock2.h BEFORE postgres.h
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#endif

extern "C" {
#include <postgres.h>
#include <fmgr.h>
#include <utils/builtins.h>
#include <funcapi.h>
#include <access/htup_details.h>
#include <catalog/pg_type.h>
}

#include <string>
#include <vector>
#include <memory>
#include <stdexcept>

namespace Hartonomous::PG {

/**
 * @brief RAII wrapper for PostgreSQL text
 */
class TextWrapper {
public:
    explicit TextWrapper(text* pg_text);
    explicit TextWrapper(const std::string& str);

    std::string to_string() const;
    text* to_pg_text() const;

    ~TextWrapper() = default;

private:
    text* pg_text_;
    bool owned_;
};

/**
 * @brief RAII wrapper for PostgreSQL bytea (binary data)
 */
class ByteaWrapper {
public:
    explicit ByteaWrapper(bytea* pg_bytea);
    explicit ByteaWrapper(const std::vector<uint8_t>& data);

    std::vector<uint8_t> to_vector() const;
    bytea* to_pg_bytea() const;

    ~ByteaWrapper() = default;

private:
    bytea* pg_bytea_;
    bool owned_;
};

/**
 * @brief Exception wrapper for PostgreSQL errors
 */
class PostgresException : public std::runtime_error {
public:
    explicit PostgresException(const std::string& msg);

    void report_to_postgres(int elevel = ERROR) const;
};

/**
 * @brief Helper for building result tuples
 */
class TupleBuilder {
public:
    TupleBuilder(FunctionCallInfo fcinfo);

    void add_text(const std::string& value);
    void add_int32(int32_t value);
    void add_int64(int64_t value);
    void add_float8(double value);
    void add_bytea(const std::vector<uint8_t>& value);
    void add_null();

    HeapTuple build();

private:
    FunctionCallInfo fcinfo_;
    std::vector<Datum> values_;
    std::vector<bool> nulls_;
    TupleDesc tupdesc_;
};

/**
 * @brief Memory context manager for PostgreSQL
 */
class MemoryContext {
public:
    explicit MemoryContext(const char* name);
    ~MemoryContext();

    void* alloc(size_t size);
    void switch_to();
    void reset();

private:
    MemoryContextData* context_;
    MemoryContextData* old_context_;
};

/**
 * @brief Generic function wrapper with exception handling
 */
template<typename Func>
Datum safe_call(FunctionCallInfo fcinfo, Func&& func) {
    try {
        return func(fcinfo);
    } catch (const PostgresException& e) {
        e.report_to_postgres();
        PG_RETURN_NULL();
    } catch (const std::exception& e) {
        PostgresException pg_ex(e.what());
        pg_ex.report_to_postgres();
        PG_RETURN_NULL();
    } catch (...) {
        PostgresException pg_ex("Unknown C++ exception");
        pg_ex.report_to_postgres();
        PG_RETURN_NULL();
    }
}

} // namespace Hartonomous::PG
