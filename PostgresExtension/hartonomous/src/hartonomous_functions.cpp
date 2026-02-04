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
    return safe_call(fcinfo, [fcinfo](FunctionCallInfo) -> Datum {
        TextWrapper tw("0.1.0");
        PG_RETURN_TEXT_P(tw.to_pg_text());
    });
}

// ==============================================================================
//  BLAKE3 Hashing
// ==============================================================================

extern "C" Datum blake3_hash(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [fcinfo](FunctionCallInfo inner_fcinfo) -> Datum {
        text* input_ptr = PG_GETARG_TEXT_PP(0);
        TextWrapper tw(input_ptr);
        std::string input = tw.to_string();

        auto hash = BLAKE3Pipeline::hash(input.data(), input.size());

        std::vector<uint8_t> hash_vec(hash.begin(), hash.end());
        ByteaWrapper bw(hash_vec);
        PG_RETURN_BYTEA_P(bw.to_pg_bytea());
    });
}

extern "C" Datum blake3_hash_codepoint(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [fcinfo](FunctionCallInfo inner_fcinfo) -> Datum {
        int32_t codepoint = PG_GETARG_INT32(0);

        auto hash = BLAKE3Pipeline::hash_codepoint(static_cast<char32_t>(codepoint));

        std::vector<uint8_t> hash_vec(hash.begin(), hash.end());
        ByteaWrapper bw(hash_vec);
        PG_RETURN_BYTEA_P(bw.to_pg_bytea());
    });
}

// ==============================================================================
//  Codepoint Projection
// ==============================================================================

extern "C" Datum codepoint_to_s3(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [fcinfo](FunctionCallInfo inner_fcinfo) -> Datum {
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

extern "C" Datum codepoint_to_hilbert(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [fcinfo](FunctionCallInfo inner_fcinfo) -> Datum {
        int32_t codepoint = PG_GETARG_INT32(0);
        auto projection = hartonomous::unicode::CodepointProjection::project(static_cast<char32_t>(codepoint));
        
        // S3 coordinates are [-1, 1]. Hilbert expects [0, 1].
        Eigen::Vector4d unit_coords;
        for (int i = 0; i < 4; ++i) {
            unit_coords[i] = (projection.s3_position[i] + 1.0) / 2.0;
        }
        
        // Use the Engine's Hilbert curve logic
        auto hilbert = hartonomous::spatial::HilbertCurve4D::encode(
            unit_coords, 
            hartonomous::spatial::HilbertCurve4D::EntityType::Atom
        );
        
        // Pack into UINT128 (16-byte bytea)
        std::vector<uint8_t> bytes(16);
        uint64_t hi_be = htobe64(hilbert.hi);
        uint64_t lo_be = htobe64(hilbert.lo);
        std::memcpy(bytes.data(), &hi_be, 8);
        std::memcpy(bytes.data() + 8, &lo_be, 8);
        
        ByteaWrapper bw(bytes);
        PG_RETURN_BYTEA_P(bw.to_pg_bytea());
    });
}

// ==============================================================================
//  Centroid Computation
// ==============================================================================

extern "C" Datum compute_centroid(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [fcinfo](FunctionCallInfo inner_fcinfo) -> Datum {
        ArrayType* input_array = PG_GETARG_ARRAYTYPE_P(0);
        
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
    return safe_call(fcinfo, [fcinfo](FunctionCallInfo inner_fcinfo) -> Datum {
        text* input_ptr = PG_GETARG_TEXT_PP(0);
        TextWrapper tw(input_ptr);
        std::string input = tw.to_string();

        TupleDesc tupdesc;
        if (get_call_result_type(inner_fcinfo, nullptr, &tupdesc) != TYPEFUNC_COMPOSITE) {
            throw PostgresException("Function must return composite type");
        }

        Datum values[6];
        bool nulls[6] = {false, false, false, false, false, false};

        values[0] = Int64GetDatum(0);  
        values[1] = Int64GetDatum(0);  
        values[2] = Int64GetDatum(0);  
        values[3] = Int64GetDatum(input.size());  
        values[4] = Int64GetDatum(0);  
        values[5] = Float8GetDatum(0.0);  

        HeapTuple tuple = heap_form_tuple(tupdesc, values, nulls);
        PG_RETURN_DATUM(HeapTupleGetDatum(tuple));
    });
}

// ==============================================================================
//  Semantic Query
// ==============================================================================

extern "C" Datum semantic_search(PG_FUNCTION_ARGS) {
    return safe_call(fcinfo, [fcinfo](FunctionCallInfo inner_fcinfo) -> Datum {
        text* query_ptr = PG_GETARG_TEXT_PP(0);
        TextWrapper tw(query_ptr);
        std::string query = tw.to_string();

        PG_RETURN_NULL();
    });
}