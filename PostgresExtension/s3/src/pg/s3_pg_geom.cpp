extern "C" {
#include "postgres.h"
#include "fmgr.h"
#include "utils/geo_decls.h"
#include "varatt.h"
#include "lwgeom_pg.h"
}

#include "s3_pg_geom.hpp"
#include <stdexcept>

namespace s3_pg
{
    // Expects already-detoasted GSERIALIZED*
    s3::Vec4 geom_to_vec4(const void* gserialized)
    {
        s3::Vec4 v{0.0, 0.0, 0.0, 0.0};

        if (!gserialized) return v;

        GSERIALIZED* g = (GSERIALIZED*)gserialized;

        // Use gserialized_peek_first_point for efficient direct access
        // This avoids full LWGEOM conversion and is faster for POINTZM
        POINT4D p;
        if (gserialized_peek_first_point(g, &p) == LW_SUCCESS) {
            v[0] = p.x;
            v[1] = p.y;
            v[2] = p.z;
            v[3] = p.m;
        }

        return v;
    }

    // Extract Vec4 from a Datum, handling detoasting properly
    s3::Vec4 datum_to_vec4(Datum gsdatum)
    {
        s3::Vec4 v{0.0, 0.0, 0.0, 0.0};

        void* ptr = DatumGetPointer(gsdatum);
        if (!ptr) return v;

        // Always detoast to be safe - PostGIS geometries may be TOASTed
        GSERIALIZED* g = (GSERIALIZED*)PG_DETOAST_DATUM(gsdatum);
        if (!g) return v;

        // Extract point coordinates
        POINT4D p;
        if (gserialized_peek_first_point(g, &p) == LW_SUCCESS) {
            v[0] = p.x;
            v[1] = p.y;
            v[2] = p.z;
            v[3] = p.m;
        }

        // Always free the detoasted copy (PG_DETOAST_DATUM may return original if not toasted)
        if ((void*)g != ptr) {
            pfree(g);
        }

        return v;
    }
}
