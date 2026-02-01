#pragma once

extern "C" {
#include "postgres.h"
#include "fmgr.h"
}

#include "geometry/s3_vec.hpp"

namespace s3_pg
{
    s3::Vec4 geom_to_vec4(const void* gserialized);
    s3::Vec4 datum_to_vec4(Datum gsdatum);
}
