/**
 * @file pg_wrapper.hpp
 * @brief PostgreSQL C API wrapper - Lean bridge between PostgreSQL and C++ Engine
 */

#pragma once

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
#include <varatt.h>
}

#include <string>
#include <vector>
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

    ~TextWrapper();

private:
    text* pg_text_;
    text* original_;
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

    ~ByteaWrapper();

private:
    bytea* pg_bytea_;
    bytea* original_;
};

/**
 * @brief Exception for PostgreSQL integration
 */
class PostgresException : public std::runtime_error {
public:
    explicit PostgresException(const std::string& msg) : std::runtime_error(msg) {}
    void report(int elevel = ERROR) const {
        ereport(elevel, (errcode(ERRCODE_EXTERNAL_ROUTINE_EXCEPTION), errmsg("%s", what())));
    }
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
    } catch (const std::exception& e) {
        ereport(ERROR, (errcode(ERRCODE_EXTERNAL_ROUTINE_EXCEPTION), errmsg("%s", e.what())));
        PG_RETURN_NULL();
    } catch (...) {
        ereport(ERROR, (errcode(ERRCODE_EXTERNAL_ROUTINE_EXCEPTION), errmsg("Unknown C++ exception")));
        PG_RETURN_NULL();
    }
}

} // namespace Hartonomous::PG