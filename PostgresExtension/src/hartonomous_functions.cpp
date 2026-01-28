/**
 * @file hartonomous_functions.cpp
 * @brief PostgreSQL function implementations for Hartonomous extension
 */

#include "hartonomous_functions.hpp"
#include "pg_wrapper.hpp"

#include <hashing/blake3_pipeline.hpp>
#include <unicode/codepoint_projection.hpp>
#include <geometry/super_fibonacci.hpp>
#include <spatial/hilbert_curve_4d.hpp>

extern "C" {
#include <utils/array.h>
#include <utils/lsyscache.h>
#include <catalog/pg_type.h>
}

using namespace Hartonomous;
using namespace Hartonomous::PG;

// ==============================================================================
//  Function Info Declarations - MUST be extern "C"
// ==============================================================================

extern "C" {
PG_FUNCTION_INFO_V1(hartonomous_version);
PG_FUNCTION_INFO_V1(blake3_hash);
PG_FUNCTION_INFO_V1(blake3_hash_codepoint);
PG_FUNCTION_INFO_V1(codepoint_to_s3);
PG_FUNCTION_INFO_V1(codepoint_to_hilbert);
PG_FUNCTION_INFO_V1(compute_centroid);
PG_FUNCTION_INFO_V1(ingest_text);
PG_FUNCTION_INFO_V1(semantic_search);
}

// ==============================================================================
//  Version Info
// ==============================================================================

extern "C" Datum hartonomous_version(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo) -> Datum {
        TextWrapper tw("0.1.0");
        PG_RETURN_TEXT_P(tw.to_pg_text());
    });
}

// ==============================================================================
//  BLAKE3 Hashing
// ==============================================================================

extern "C" Datum blake3_hash(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo fcinfo) -> Datum {
        // Get input text
        text* input_text = PG_GETARG_TEXT_PP(0);
        TextWrapper tw(input_text);
        std::string input = tw.to_string();

        // Hash it
        auto hash = BLAKE3Pipeline::hash(input.data(), input.size());

        // Return as bytea
        std::vector<uint8_t> hash_vec(hash.begin(), hash.end());
        ByteaWrapper bw(hash_vec);
        PG_RETURN_BYTEA_P(bw.to_pg_bytea());
    });
}

extern "C" Datum blake3_hash_codepoint(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo fcinfo) -> Datum {
        // Get codepoint as int32
        int32_t codepoint = PG_GETARG_INT32(0);

        // Hash it
        auto hash = BLAKE3Pipeline::hash_codepoint(static_cast<char32_t>(codepoint));

        // Return as bytea
        std::vector<uint8_t> hash_vec(hash.begin(), hash.end());
        ByteaWrapper bw(hash_vec);
        PG_RETURN_BYTEA_P(bw.to_pg_bytea());
    });
}

// ==============================================================================
//  Codepoint Projection
// ==============================================================================

extern "C" Datum codepoint_to_s3(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo fcinfo) -> Datum {
        // Get codepoint
        int32_t codepoint = PG_GETARG_INT32(0);

        // Project to S³
        auto result = hartonomous::unicode::CodepointProjection::project(
            static_cast<char32_t>(codepoint)
        );

        // Build result tuple (x, y, z, w)
        TupleDesc tupdesc;
        if (get_call_result_type(fcinfo, nullptr, &tupdesc) != TYPEFUNC_COMPOSITE) {
            throw PostgresException("Function must return composite type");
        }

        Datum values[4];
        bool nulls[4] = {false, false, false, false};

        values[0] = Float8GetDatum(result.s3_position[0]);
        values[1] = Float8GetDatum(result.s3_position[1]);
        values[2] = Float8GetDatum(result.s3_position[2]);
        values[3] = Float8GetDatum(result.s3_position[3]);

        HeapTuple tuple = heap_form_tuple(tupdesc, values, nulls);
        PG_RETURN_DATUM(HeapTupleGetDatum(tuple));
    });
}

extern "C" Datum codepoint_to_hilbert(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo fcinfo) -> Datum {
        // Get codepoint
        int32_t codepoint = PG_GETARG_INT32(0);

        // Project and get Hilbert index
        auto result = hartonomous::unicode::CodepointProjection::project(
            static_cast<char32_t>(codepoint)
        );

        PG_RETURN_INT64(result.hilbert_index);
    });
}

// ==============================================================================
//  Centroid Computation
// ==============================================================================

extern "C" Datum compute_centroid(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo fcinfo) -> Datum {
        // Get array of points (array of composite types with x,y,z,w)
        ArrayType* array = DatumGetArrayTypeP(PG_GETARG_DATUM(0));

        // Get array element type info
        Oid element_type = ARR_ELEMTYPE(array);
        int16 typlen;
        bool typbyval;
        char typalign;
        get_typlenbyvalalign(element_type, &typlen, &typbyval, &typalign);

        // Decode array
        int nelems;
        Datum* elems;
        bool* nulls;

        deconstruct_array(array, element_type, typlen, typbyval, typalign,
                         &elems, &nulls, &nelems);

        if (nelems == 0) {
            throw PostgresException("Cannot compute centroid of empty array");
        }

        // Sum all points
        Eigen::Vector4d sum = Eigen::Vector4d::Zero();

        for (int i = 0; i < nelems; i++) {
            if (nulls[i]) continue;

            HeapTupleHeader tup_header = DatumGetHeapTupleHeader(elems[i]);

            // Get tuple descriptor from the tuple header
            Oid tupType = HeapTupleHeaderGetTypeId(tup_header);
            int32 tupTypmod = HeapTupleHeaderGetTypMod(tup_header);
            TupleDesc tupdesc = lookup_rowtype_tupdesc(tupType, tupTypmod);

            Datum values[4];
            bool val_nulls[4];

            // Build a HeapTupleData structure to pass to heap_deform_tuple
            HeapTupleData tup_data;
            tup_data.t_len = HeapTupleHeaderGetDatumLength(tup_header);
            tup_data.t_data = tup_header;
            ItemPointerSetInvalid(&(tup_data.t_self));
            tup_data.t_tableOid = InvalidOid;

            heap_deform_tuple(&tup_data, tupdesc, values, val_nulls);

            ReleaseTupleDesc(tupdesc);

            if (!val_nulls[0] && !val_nulls[1] && !val_nulls[2] && !val_nulls[3]) {
                sum[0] += DatumGetFloat8(values[0]);
                sum[1] += DatumGetFloat8(values[1]);
                sum[2] += DatumGetFloat8(values[2]);
                sum[3] += DatumGetFloat8(values[3]);
            }
        }

        // Normalize to S³ surface
        double norm = sum.norm();
        if (norm > 0) {
            sum /= norm;
        }

        // Return as composite (x, y, z, w)
        TupleDesc tupdesc;
        if (get_call_result_type(fcinfo, nullptr, &tupdesc) != TYPEFUNC_COMPOSITE) {
            throw PostgresException("Function must return composite type");
        }

        Datum result_values[4];
        bool result_nulls[4] = {false, false, false, false};

        result_values[0] = Float8GetDatum(sum[0]);
        result_values[1] = Float8GetDatum(sum[1]);
        result_values[2] = Float8GetDatum(sum[2]);
        result_values[3] = Float8GetDatum(sum[3]);

        HeapTuple result_tuple = heap_form_tuple(tupdesc, result_values, result_nulls);
        PG_RETURN_DATUM(HeapTupleGetDatum(result_tuple));
    });
}

// ==============================================================================
//  Text Ingestion
// ==============================================================================

extern "C" Datum ingest_text(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo fcinfo) -> Datum {
        // Get input text
        text* input_text = PG_GETARG_TEXT_PP(0);
        TextWrapper tw(input_text);
        std::string input = tw.to_string();

        // For now, just return mock stats
        // TODO: Implement actual ingestion with database connection
        TupleDesc tupdesc;
        if (get_call_result_type(fcinfo, nullptr, &tupdesc) != TYPEFUNC_COMPOSITE) {
            throw PostgresException("Function must return composite type");
        }

        Datum values[6];
        bool nulls[6] = {false, false, false, false, false, false};

        values[0] = Int64GetDatum(0);  // atoms_new
        values[1] = Int64GetDatum(0);  // compositions_new
        values[2] = Int64GetDatum(0);  // relations_new
        values[3] = Int64GetDatum(input.size());  // original_bytes
        values[4] = Int64GetDatum(0);  // stored_bytes
        values[5] = Float8GetDatum(0.0);  // compression_ratio

        HeapTuple tuple = heap_form_tuple(tupdesc, values, nulls);
        PG_RETURN_DATUM(HeapTupleGetDatum(tuple));
    });
}

// ==============================================================================
//  Semantic Query
// ==============================================================================

extern "C" Datum semantic_search(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo fcinfo) -> Datum {
        // Get query text
        text* query_text = PG_GETARG_TEXT_PP(0);
        TextWrapper tw(query_text);
        std::string query = tw.to_string();

        // TODO: Implement actual semantic search
        // For now, just return empty result

        PG_RETURN_NULL();
    });
}
