/**
 * @file hartonomous_shim.c
 * @brief Pure C shim between PostgreSQL and Hartonomous Engine C API.
 */

#include "postgres.h"
#include "fmgr.h"
#include "utils/builtins.h"
#include "utils/bytea.h"
#include "varatt.h"
#include "funcapi.h"
#include "interop_api.h"
#include <string.h>

PG_MODULE_MAGIC;

// =============================================================================
//  Version Info
// =============================================================================

PG_FUNCTION_INFO_V1(hartonomous_version);
Datum hartonomous_version(PG_FUNCTION_ARGS) {
    const char* version = hartonomous_get_version();
    PG_RETURN_TEXT_P(cstring_to_text(version));
}

// =============================================================================
//  BLAKE3 Hashing
// =============================================================================

PG_FUNCTION_INFO_V1(blake3_hash);
Datum blake3_hash(PG_FUNCTION_ARGS) {
    struct varlena *v = PG_DETOAST_DATUM_PACKED(PG_GETARG_DATUM(0));
    const char* data = VARDATA_ANY(v);
    size_t len = VARSIZE_ANY_EXHDR(v);
    
    bytea *res = (bytea *) palloc(16 + VARHDRSZ);
    SET_VARSIZE(res, 16 + VARHDRSZ);
    
    hartonomous_blake3_hash(data, len, (uint8_t*)VARDATA(res));
    
    PG_FREE_IF_COPY(v, 0);
    PG_RETURN_BYTEA_P(res);
}

PG_FUNCTION_INFO_V1(blake3_hash_codepoint);
Datum blake3_hash_codepoint(PG_FUNCTION_ARGS) {
    uint32_t codepoint = (uint32_t)PG_GETARG_INT32(0);
    
    bytea *res = (bytea *) palloc(16 + VARHDRSZ);
    SET_VARSIZE(res, 16 + VARHDRSZ);
    
    hartonomous_blake3_hash_codepoint(codepoint, (uint8_t*)VARDATA(res));
    
    PG_RETURN_BYTEA_P(res);
}

// =============================================================================
//  Codepoint Projection
// =============================================================================

PG_FUNCTION_INFO_V1(codepoint_to_s3);
Datum codepoint_to_s3(PG_FUNCTION_ARGS) {
    uint32_t codepoint = (uint32_t)PG_GETARG_INT32(0);
    double coords[4];
    
    if (!hartonomous_codepoint_to_s3(codepoint, coords)) {
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Invalid codepoint")));
    }
    
    char buf[256];
    snprintf(buf, sizeof(buf), "POINT ZM(%.15f %.15f %.15f %.15f)", coords[0], coords[1], coords[2], coords[3]);
    
    PG_RETURN_TEXT_P(cstring_to_text(buf));
}

PG_FUNCTION_INFO_V1(codepoint_to_hilbert);
Datum codepoint_to_hilbert(PG_FUNCTION_ARGS) {
    uint32_t codepoint = (uint32_t)PG_GETARG_INT32(0);
    double coords[4];
    uint64_t hi, lo;
    
    if (!hartonomous_codepoint_to_s3(codepoint, coords)) {
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("Invalid codepoint")));
    }
    
    // EntityType::Atom = 1
    hartonomous_s3_to_hilbert(coords, 1, &hi, &lo);
    
    bytea *res = (bytea *) palloc(16 + VARHDRSZ);
    SET_VARSIZE(res, 16 + VARHDRSZ);
    
    uint64_t hi_be = htobe64(hi);
    uint64_t lo_be = htobe64(lo);
    
    memcpy(VARDATA(res), &hi_be, 8);
    memcpy(VARDATA(res) + 8, &lo_be, 8);
    
    PG_RETURN_BYTEA_P(res);
}

#include <utils/array.h>
#include <catalog/pg_type.h>

// =============================================================================
//  Analysis
// =============================================================================

PG_FUNCTION_INFO_V1(compute_centroid);
Datum compute_centroid(PG_FUNCTION_ARGS) {
    ArrayType* input_array = PG_GETARG_ARRAYTYPE_P(0);
    
    int nelems;
    Datum* elems;
    bool* nulls;
    deconstruct_array(input_array, FLOAT8OID, 8, true, 'd', &elems, &nulls, &nelems);

    if (nelems == 0) PG_RETURN_NULL();
    
    // We expect a flat array of doubles (size must be multiple of 4)
    if (nelems % 4 != 0) {
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), 
                errmsg("Array size must be a multiple of 4 for S3 points")));
    }

    size_t point_count = nelems / 4;
    double* points = (double*)palloc(nelems * sizeof(double));
    for (int i = 0; i < nelems; i++) {
        points[i] = DatumGetFloat8(elems[i]);
    }

    double res[4];
    hartonomous_s3_compute_centroid(points, point_count, res);
    
    pfree(points);

    char buf[256];
    snprintf(buf, sizeof(buf), "POINT ZM(%.15f %.15f %.15f %.15f)", res[0], res[1], res[2], res[3]);
    
    PG_RETURN_TEXT_P(cstring_to_text(buf));
}

// =============================================================================
//  Ingestion
// =============================================================================

PG_FUNCTION_INFO_V1(ingest_text);
Datum ingest_text(PG_FUNCTION_ARGS) {
    struct varlena *v = PG_DETOAST_DATUM_PACKED(PG_GETARG_DATUM(0));
    const char* input_text = VARDATA_ANY(v);
    
    TupleDesc tupdesc;
    if (get_call_result_type(fcinfo, NULL, &tupdesc) != TYPEFUNC_COMPOSITE) {
        PG_FREE_IF_COPY(v, 0);
        ereport(ERROR, (errcode(ERRCODE_FEATURE_NOT_SUPPORTED), errmsg("Function must return a composite type")));
    }

    // Initialize Engine Connection
    h_db_connection_t conn = hartonomous_db_create(NULL); // Uses env vars
    if (!conn) {
        PG_FREE_IF_COPY(v, 0);
        ereport(ERROR, (errcode(ERRCODE_CONNECTION_FAILURE), errmsg("Failed to connect to engine: %s", hartonomous_get_last_error())));
    }

    h_ingester_t ingester = hartonomous_ingester_create(conn);
    if (!ingester) {
        hartonomous_db_destroy(conn);
        PG_FREE_IF_COPY(v, 0);
        ereport(ERROR, (errcode(ERRCODE_INTERNAL_ERROR), errmsg("Failed to create ingester: %s", hartonomous_get_last_error())));
    }

    HIngestionStats stats;
    if (!hartonomous_ingest_text(ingester, input_text, &stats)) {
        hartonomous_ingester_destroy(ingester);
        hartonomous_db_destroy(conn);
        PG_FREE_IF_COPY(v, 0);
        ereport(ERROR, (errcode(ERRCODE_INTERNAL_ERROR), errmsg("Ingestion failed: %s", hartonomous_get_last_error())));
    }

    Datum values[6];
    bool nulls[6] = {false, false, false, false, false, false};

    values[0] = Int64GetDatum((int64_t)stats.atoms_new);
    values[1] = Int64GetDatum((int64_t)stats.compositions_new);
    values[2] = Int64GetDatum((int64_t)stats.relations_new);
    values[3] = Int64GetDatum((int64_t)stats.original_bytes);
    values[4] = Int64GetDatum((int64_t)stats.stored_bytes);
    values[5] = Float8GetDatum(stats.compression_ratio);

    HeapTuple tuple = heap_form_tuple(tupdesc, values, nulls);
    
    hartonomous_ingester_destroy(ingester);
    hartonomous_db_destroy(conn);
    PG_FREE_IF_COPY(v, 0);

    PG_RETURN_DATUM(HeapTupleGetDatum(tuple));
}