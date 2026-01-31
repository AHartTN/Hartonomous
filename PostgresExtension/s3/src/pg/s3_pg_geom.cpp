extern "C" {
#include "postgres.h"
#include "fmgr.h"
#include "utils/geo_decls.h"
#include "liblwgeom.h"
}

#include "s3_pg_geom.hpp"
#include <stdexcept>

namespace s3_pg
{
    s3::Vec4 geom_to_vec4(const void* gserialized)
    {
        s3::Vec4 v{0.0, 0.0, 0.0, 0.0};
        
        if (!gserialized) return v;

        // Cast to GSERIALIZED* (PostGIS internal format)
        GSERIALIZED* g = (GSERIALIZED*)gserialized;

        // Convert GSERIALIZED to LWGEOM
        LWGEOM* lw = lwgeom_from_gserialized(g);
        if (!lw) return v;

        // Ensure it's a point and has enough dimensions
        if (lw->type == POINTTYPE) {
            LWPOINT* point = (LWPOINT*)lw;
            POINT4D p;
            if (getPoint4d_p(point->point, 0, &p)) {
                v[0] = p.x;
                v[1] = p.y;
                v[2] = p.z;
                v[3] = p.m;
            }
        }

        lwgeom_free(lw);
        return v;
    }
}
