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
    // Extract PostGIS geometry safely
    Datum da = PG_GETARG_DATUM(0);
    Datum db = PG_GETARG_DATUM(1);

    // Convert to Vec4 using the safe detoasting helper
    s3::Vec4 a = s3_pg::datum_to_vec4(da);
    s3::Vec4 b = s3_pg::datum_to_vec4(db);

    // Call your engine’s canonical S³ geodesic distance
    double d = s3::geodesic_distance(a, b);

    PG_RETURN_FLOAT8(d);
}

}
