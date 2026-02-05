extern "C" {
#include "postgres.h"
#include "fmgr.h"
#include "utils/geo_decls.h"
#include "utils/varlena.h"

#include "liblwgeom.h"   // for GSERIALIZED, POINT4D, gserialized_peek_first_point
#include "lwgeom_pg.h"   // PostGIS PG glue (needed in most PG-side code)
}

#include "s3_pg_geom.hpp"
#include <stdexcept>

namespace s3_pg
{
    // Expects already-detoasted GSERIALIZED*
    s3::Vec4 geom_to_vec4(const GSERIALIZED* gserialized)
    {
        s3::Vec4 v{0.0, 0.0, 0.0, 0.0};

        if (!gserialized)
            return v;

        POINT4D p;
        if (gserialized_peek_first_point(gserialized, &p) == LW_SUCCESS)
        {
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

        if (DatumGetPointer(gsdatum) == nullptr)
            return v;

        // Detoast geometry; may return original pointer or a copied varlena
        GSERIALIZED* g = reinterpret_cast<GSERIALIZED*>(PG_DETOAST_DATUM(gsdatum));

        POINT4D p;
        if (g && gserialized_peek_first_point(g, &p) == LW_SUCCESS)
        {
            v[0] = p.x;
            v[1] = p.y;
            v[2] = p.z;
            v[3] = p.m;
        }

        // Free only if a copy was made
        if (reinterpret_cast<void*>(g) != DatumGetPointer(gsdatum))
        {
            pfree(g);
        }

        return v;
    }
}
