#pragma once

extern "C" {
#include "postgres.h"
#include "fmgr.h"
#include "liblwgeom.h" // Provides GSERIALIZED typedef
}

#include "geometry/s3_vec.hpp"

namespace s3_pg
{
    // Expects an already-detoasted GSERIALIZED*
    s3::Vec4 geom_to_vec4(const GSERIALIZED* gserialized);

    // Safely extracts Vec4 from a Datum containing a PostGIS geometry
    s3::Vec4 datum_to_vec4(Datum gsdatum);
}
