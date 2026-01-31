#pragma once

extern "C" {
#include "postgres.h"
#include "fmgr.h"
#include "access/gist.h"
#include "access/skey.h"
}

#include "geometry/s3_bbox.hpp"
#include "s3_pg_geom.hpp"

extern "C" {

PGDLLEXPORT Datum gist_s3_compress(PG_FUNCTION_ARGS);
PGDLLEXPORT Datum gist_s3_decompress(PG_FUNCTION_ARGS);
PGDLLEXPORT Datum gist_s3_consistent(PG_FUNCTION_ARGS);
PGDLLEXPORT Datum gist_s3_union(PG_FUNCTION_ARGS);
PGDLLEXPORT Datum gist_s3_penalty(PG_FUNCTION_ARGS);
PGDLLEXPORT Datum gist_s3_picksplit(PG_FUNCTION_ARGS);
PGDLLEXPORT Datum gist_s3_same(PG_FUNCTION_ARGS);
PGDLLEXPORT Datum gist_s3_distance(PG_FUNCTION_ARGS);

}
