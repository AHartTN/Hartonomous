extern "C" {
#include "postgres.h"
#include "fmgr.h"
#include "catalog/pg_type.h"
}

#include "geometry/s3_distance.hpp"
#include "s3_pg_geom.hpp"

extern "C" {

PG_MODULE_MAGIC;

PG_FUNCTION_INFO_V1(geodesic_distance_s3_c);
Datum geodesic_distance_s3_c(PG_FUNCTION_ARGS)
{
    void* ga = PG_GETARG_POINTER(0);
    void* gb = PG_GETARG_POINTER(1);

    s3::Vec4 a = s3_pg::geom_to_vec4(ga);
    s3::Vec4 b = s3_pg::geom_to_vec4(gb);

    double d = s3::geodesic_distance(a, b);
    PG_RETURN_FLOAT8(d);
}

}
