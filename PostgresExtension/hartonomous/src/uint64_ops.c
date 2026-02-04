#include "postgres.h"
#include "fmgr.h"
#include "utils/bytea.h"
#include <stdint.h>
#include <arpa/inet.h>

extern "C" {

PG_FUNCTION_INFO_V1(uint64_add);
Datum uint64_add(PG_FUNCTION_ARGS)
{
    bytea* a = PG_GETARG_BYTEA_P(0);
    bytea* b = PG_GETARG_BYTEA_P(1);

    if (VARSIZE(a) - VARHDRSZ != 8 || VARSIZE(b) - VARHDRSZ != 8)
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("UINT64 must be 8 bytes")));

    uint64_t val_a, val_b;
    memcpy(&val_a, VARDATA(a), 8);
    memcpy(&val_b, VARDATA(b), 8);

    // Convert from big-endian (storage format) to host order
    val_a = __builtin_bswap64(val_a);
    val_b = __builtin_bswap64(val_b);

    uint64_t result = val_a + val_b;
    result = __builtin_bswap64(result);

    bytea* res = (bytea*) palloc(8 + VARHDRSZ);
    SET_VARSIZE(res, 8 + VARHDRSZ);
    memcpy(VARDATA(res), &result, 8);

    PG_RETURN_BYTEA_P(res);
}

PG_FUNCTION_INFO_V1(uint64_to_double);
Datum uint64_to_double(PG_FUNCTION_ARGS)
{
    bytea* a = PG_GETARG_BYTEA_P(0);
    if (VARSIZE(a) - VARHDRSZ != 8)
        PG_RETURN_FLOAT8(0.0);

    uint64_t val;
    memcpy(&val, VARDATA(a), 8);
    val = __builtin_bswap64(val);

    PG_RETURN_FLOAT8((double)val);
}

}
