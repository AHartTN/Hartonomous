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
#include <iomanip>

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
        int32_t codepoint = PG_GETARG_INT32(0);
        auto result = hartonomous::unicode::CodepointProjection::project(static_cast<char32_t>(codepoint));

        std::ostringstream wkt;
        wkt << std::fixed << std::setprecision(15) 
            << "POINT ZM(" 
            << result.s3_position[0] << " " 
            << result.s3_position[1] << " " 
            << result.s3_position[2] << " " 
            << result.s3_position[3] << ")";

        TextWrapper tw(wkt.str());
        PG_RETURN_TEXT_P(tw.to_pg_text());
    });
}

// ... (codepoint_to_hilbert remains as is, returning int8 array is fine for that custom type, or we can make it return text if HILBERT128 is text)

// ==============================================================================
//  Centroid Computation
// ==============================================================================

extern "C" Datum compute_centroid(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [](FunctionCallInfo fcinfo) -> Datum {
        // Input: Array of text (WKT) or bytea (WKB). 
        // Assuming the input is now an array of GEOMETRY (which comes in as bytea/serialized).
        // Without liblwgeom, parsing serialized geometry is hard. 
        // BETTER: Input array of float8[] (4 coords) OR array of text WKT.
        // Let's stick to array of float8[] for input flexibility from C++ side, 
        // OR better: accept array of text WKT to be consistent.
        
        // Actually, if we want to be "PostGIS Native", the input should be GEOMETRY[].
        // But we can't parse GEOMETRY without liblwgeom.
        // Compromise: The input to this C function is likely coming from our own logic or text.
        // Let's accept float8[] points for calculation, return WKT.
        
        ArrayType* input_array = PG_GETARG_ARRAYTYPE_P(0);
        // ... (Deconstruction logic similar to before to get points)
        // For simplicity in this step, let's keep the input processing generic
        // but ensure the OUTPUT is WKT.

        // ... (Re-using the previous deconstruction logic but outputting WKT)

        int nelems;
        Datum* elems;
        bool* nulls;
        deconstruct_array(input_array, ARR_ELEMTYPE(input_array), -1, false, 'd',
                         &elems, &nulls, &nelems);

        Eigen::Vector4d sum = Eigen::Vector4d::Zero();
        int valid_points = 0;

        for (int i = 0; i < nelems; i++) {
            if (nulls[i]) continue;
            ArrayType* point_arr = DatumGetArrayTypeP(elems[i]);
            int point_dims;
            Datum* point_vals;
            bool* point_nulls;
            deconstruct_array(point_arr, ARR_ELEMTYPE(point_arr), -1, false, 'd',
                            &point_vals, &point_nulls, &point_dims);

            if (point_dims >= 4) {
                sum[0] += DatumGetFloat8(point_vals[0]);
                sum[1] += DatumGetFloat8(point_vals[1]);
                sum[2] += DatumGetFloat8(point_vals[2]);
                sum[3] += DatumGetFloat8(point_vals[3]);
                valid_points++;
            }
        }

        if (valid_points == 0) PG_RETURN_NULL();

        double norm = sum.norm();
        if (norm > 0) sum /= norm;

        std::ostringstream wkt;
        wkt << std::fixed << std::setprecision(15) 
            << "POINT ZM(" 
            << sum[0] << " " << sum[1] << " " << sum[2] << " " << sum[3] << ")";

        TextWrapper tw(wkt.str());
        PG_RETURN_TEXT_P(tw.to_pg_text());
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