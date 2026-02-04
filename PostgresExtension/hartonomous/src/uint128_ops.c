#include "postgres.h"
#include "fmgr.h"
#include "varatt.h"
#include "utils/bytea.h"
#include <stdint.h>
#include <endian.h>

/*
 * UINT128 operators using standard PostgreSQL macros
 */

PG_FUNCTION_INFO_V1(uint128_from_parts);
Datum
uint128_from_parts(PG_FUNCTION_ARGS)
{
    uint64_t hi = htobe64((uint64_t)PG_GETARG_INT64(0));
    uint64_t lo = htobe64((uint64_t)PG_GETARG_INT64(1));
    bytea *res = (bytea *) palloc(16 + VARHDRSZ);

    SET_VARSIZE(res, 16 + VARHDRSZ);
    memcpy(VARDATA(res), &hi, 8);
    memcpy(VARDATA(res) + 8, &lo, 8);

    PG_RETURN_BYTEA_P(res);
}

PG_FUNCTION_INFO_V1(uint128_hi);
Datum
uint128_hi(PG_FUNCTION_ARGS)
{
    bytea *a = PG_GETARG_BYTEA_PP(0);
    uint64_t val;

    if (VARSIZE_ANY_EXHDR(a) != 16)
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("UINT128 must be 16 bytes")));

    memcpy(&val, VARDATA_ANY(a), 8);
    PG_RETURN_INT64((int64_t)be64toh(val));
}

PG_FUNCTION_INFO_V1(uint128_lo);
Datum
uint128_lo(PG_FUNCTION_ARGS)
{
    bytea *a = PG_GETARG_BYTEA_PP(0);
    uint64_t val;

    if (VARSIZE_ANY_EXHDR(a) != 16)
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("UINT128 must be 16 bytes")));

    memcpy(&val, VARDATA_ANY(a) + 8, 8);
    PG_RETURN_INT64((int64_t)be64toh(val));
}
