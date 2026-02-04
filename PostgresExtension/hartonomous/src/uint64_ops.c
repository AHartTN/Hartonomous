#include "postgres.h"
#include "fmgr.h"
#include "varatt.h"
#include "utils/bytea.h"
#include <stdint.h>
#include <endian.h>

/*
 * UINT64 operators using standard PostgreSQL macros
 */

PG_FUNCTION_INFO_V1(uint64_add);
Datum
uint64_add(PG_FUNCTION_ARGS)
{
    bytea *a = PG_GETARG_BYTEA_PP(0);
    bytea *b = PG_GETARG_BYTEA_PP(1);
    uint64_t val_a, val_b, res_val;
    bytea *res;

    if (VARSIZE_ANY_EXHDR(a) != 8 || VARSIZE_ANY_EXHDR(b) != 8)
        ereport(ERROR, (errcode(ERRCODE_INVALID_PARAMETER_VALUE), errmsg("UINT64 must be 8 bytes")));

    memcpy(&val_a, VARDATA_ANY(a), 8);
    memcpy(&val_b, VARDATA_ANY(b), 8);

    res_val = htobe64(be64toh(val_a) + be64toh(val_b));

    res = (bytea *) palloc(8 + VARHDRSZ);
    SET_VARSIZE(res, 8 + VARHDRSZ);
    memcpy(VARDATA(res), &res_val, 8);

    PG_RETURN_BYTEA_P(res);
}

PG_FUNCTION_INFO_V1(uint64_to_double);
Datum
uint64_to_double(PG_FUNCTION_ARGS)
{
    bytea *a = PG_GETARG_BYTEA_PP(0);
    uint64_t val;
    
    if (VARSIZE_ANY_EXHDR(a) != 8)
        PG_RETURN_FLOAT8(0.0);

    memcpy(&val, VARDATA_ANY(a), 8);
    PG_RETURN_FLOAT8((double)be64toh(val));
}