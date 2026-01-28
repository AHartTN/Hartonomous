/**
 * @file pg_wrapper.cpp
 * @brief PostgreSQL C API wrapper implementation
 */

#include "pg_wrapper.hpp"
#include <cstring>

namespace Hartonomous::PG {

// ==============================================================================
//  TextWrapper
// ==============================================================================

TextWrapper::TextWrapper(text* pg_text)
    : pg_text_(pg_text), owned_(false) {}

TextWrapper::TextWrapper(const std::string& str)
    : owned_(true) {
    size_t len = str.length();
    pg_text_ = (text*)palloc(VARHDRSZ + len);
    SET_VARSIZE(pg_text_, VARHDRSZ + len);
    std::memcpy(VARDATA(pg_text_), str.data(), len);
}

std::string TextWrapper::to_string() const {
    if (!pg_text_) return "";
    size_t len = VARSIZE(pg_text_) - VARHDRSZ;
    return std::string(VARDATA(pg_text_), len);
}

text* TextWrapper::to_pg_text() const {
    return pg_text_;
}

// ==============================================================================
//  ByteaWrapper
// ==============================================================================

ByteaWrapper::ByteaWrapper(bytea* pg_bytea)
    : pg_bytea_(pg_bytea), owned_(false) {}

ByteaWrapper::ByteaWrapper(const std::vector<uint8_t>& data)
    : owned_(true) {
    size_t len = data.size();
    pg_bytea_ = (bytea*)palloc(VARHDRSZ + len);
    SET_VARSIZE(pg_bytea_, VARHDRSZ + len);
    std::memcpy(VARDATA(pg_bytea_), data.data(), len);
}

std::vector<uint8_t> ByteaWrapper::to_vector() const {
    if (!pg_bytea_) return {};
    size_t len = VARSIZE(pg_bytea_) - VARHDRSZ;
    uint8_t* data = (uint8_t*)VARDATA(pg_bytea_);
    return std::vector<uint8_t>(data, data + len);
}

bytea* ByteaWrapper::to_pg_bytea() const {
    return pg_bytea_;
}

// ==============================================================================
//  PostgresException
// ==============================================================================

PostgresException::PostgresException(const std::string& msg)
    : std::runtime_error(msg) {}

void PostgresException::report_to_postgres(int elevel) const {
    ereport(elevel,
        (errcode(ERRCODE_EXTERNAL_ROUTINE_EXCEPTION),
         errmsg("%s", what())));
}

// ==============================================================================
//  TupleBuilder
// ==============================================================================

TupleBuilder::TupleBuilder(FunctionCallInfo fcinfo)
    : fcinfo_(fcinfo) {
    // Get tuple descriptor from function context
    if (get_call_result_type(fcinfo, nullptr, &tupdesc_) != TYPEFUNC_COMPOSITE) {
        throw PostgresException("Function must return a composite type");
    }
}

void TupleBuilder::add_text(const std::string& value) {
    TextWrapper tw(value);
    values_.push_back(PointerGetDatum(tw.to_pg_text()));
    nulls_.push_back(false);
}

void TupleBuilder::add_int32(int32_t value) {
    values_.push_back(Int32GetDatum(value));
    nulls_.push_back(false);
}

void TupleBuilder::add_int64(int64_t value) {
    values_.push_back(Int64GetDatum(value));
    nulls_.push_back(false);
}

void TupleBuilder::add_float8(double value) {
    values_.push_back(Float8GetDatum(value));
    nulls_.push_back(false);
}

void TupleBuilder::add_bytea(const std::vector<uint8_t>& value) {
    ByteaWrapper bw(value);
    values_.push_back(PointerGetDatum(bw.to_pg_bytea()));
    nulls_.push_back(false);
}

void TupleBuilder::add_null() {
    values_.push_back((Datum)0);
    nulls_.push_back(true);
}

HeapTuple TupleBuilder::build() {
    // vector<bool> doesn't have contiguous storage, so we need to convert
    size_t n = nulls_.size();
    bool* nulls_array = (bool*)palloc(n * sizeof(bool));
    for (size_t i = 0; i < n; i++) {
        nulls_array[i] = nulls_[i];
    }
    HeapTuple result = heap_form_tuple(tupdesc_, values_.data(), nulls_array);
    pfree(nulls_array);
    return result;
}

// ==============================================================================
//  MemoryContext
// ==============================================================================

MemoryContext::MemoryContext(const char* name)
    : old_context_(nullptr) {
    // PostgreSQL 18 requires compile-time constant strings for context names
    // Use a generic name since we can't use runtime strings
    (void)name;  // Suppress unused parameter warning
    context_ = AllocSetContextCreate(CurrentMemoryContext,
                                      "HartonomousContext",
                                      ALLOCSET_DEFAULT_SIZES);
}

MemoryContext::~MemoryContext() {
    if (context_) {
        MemoryContextDelete(context_);
    }
}

void* MemoryContext::alloc(size_t size) {
    return MemoryContextAlloc(context_, size);
}

void MemoryContext::switch_to() {
    old_context_ = MemoryContextSwitchTo(context_);
}

void MemoryContext::reset() {
    MemoryContextReset(context_);
}

} // namespace Hartonomous::PG
