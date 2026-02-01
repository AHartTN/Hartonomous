extern "C" {
#include "postgres.h"
#include "fmgr.h"
#include "access/gist.h"
#include "access/skey.h"
#include "catalog/pg_type.h"
#include "varatt.h"
#include "lwgeom_pg.h"
}

#include "s3_pg_gist.hpp"

extern "C" {

typedef struct
{
    int32 vl_len_;
    double min[4];
    double max[4];
} S3GistBBox;

static S3GistBBox* bbox_from_vec(const s3::BBox4& b)
{
    S3GistBBox* box = (S3GistBBox*) palloc(sizeof(S3GistBBox));
    SET_VARSIZE(box, sizeof(S3GistBBox));
    for (int i = 0; i < 4; ++i)
    {
        box->min[i] = b.min[i];
        box->max[i] = b.max[i];
    }
    return box;
}

static s3::BBox4 bbox_to_vec(const S3GistBBox* box)
{
    s3::BBox4 b;
    for (int i = 0; i < 4; ++i)
    {
        b.min[i] = box->min[i];
        b.max[i] = box->max[i];
    }
    return b;
}

PG_FUNCTION_INFO_V1(gist_s3_compress);
Datum gist_s3_compress(PG_FUNCTION_ARGS)
{
    GISTENTRY* entry = (GISTENTRY*) PG_GETARG_POINTER(0);

    if (entry->leafkey)
    {
        // Use datum_to_vec4 which properly handles detoasting
        s3::Vec4 p = s3_pg::datum_to_vec4(entry->key);
        s3::BBox4 bb = s3::bbox_from_point(p);
        S3GistBBox* box = bbox_from_vec(bb);

        GISTENTRY* retval = (GISTENTRY*) palloc(sizeof(GISTENTRY));
        gistentryinit(*retval, PointerGetDatum(box),
                      entry->rel, entry->page, entry->offset, false);
        PG_RETURN_POINTER(retval);
    }
    else
    {
        PG_RETURN_POINTER(entry);
    }
}

PG_FUNCTION_INFO_V1(gist_s3_decompress);
Datum gist_s3_decompress(PG_FUNCTION_ARGS)
{
    GISTENTRY* entry = (GISTENTRY*) PG_GETARG_POINTER(0);
    PG_RETURN_POINTER(entry);
}

PG_FUNCTION_INFO_V1(gist_s3_consistent);
Datum gist_s3_consistent(PG_FUNCTION_ARGS)
{
    GISTENTRY* entry = (GISTENTRY*) PG_GETARG_POINTER(0);
    Datum query = PG_GETARG_DATUM(1);
    StrategyNumber strategy = (StrategyNumber) PG_GETARG_UINT16(2);
    bool* recheck = (bool*) PG_GETARG_POINTER(4);

    // For KNN queries (strategy 1 = ORDER BY <=>), we use distance-based pruning
    // The distance function handles the actual ordering
    // We return true for any box that could potentially contain a closer point
    *recheck = true;

    S3GistBBox* box = (S3GistBBox*) DatumGetPointer(entry->key);
    s3::BBox4 bb = bbox_to_vec(box);

    // Use datum_to_vec4 which handles detoasting properly
    s3::Vec4 qp = s3_pg::datum_to_vec4(query);

    // For ORDER BY queries (KNN), always consistent - distance function handles ranking
    // This allows the index to be used for nearest-neighbor searches
    if (strategy == 1)
    {
        PG_RETURN_BOOL(true);
    }

    // For other strategies (future: range queries), check bbox intersection
    // Currently unused, but ready for expansion
    PG_RETURN_BOOL(true);
}

PG_FUNCTION_INFO_V1(gist_s3_union);
Datum gist_s3_union(PG_FUNCTION_ARGS)
{
    GistEntryVector* entryvec = (GistEntryVector*) PG_GETARG_POINTER(0);
    int* sizep = (int*) PG_GETARG_POINTER(1);

    s3::BBox4 acc;
    bool first = true;

    for (int i = 0; i < entryvec->n; ++i)
    {
        GISTENTRY* e = &entryvec->vector[i];
        S3GistBBox* box = (S3GistBBox*) DatumGetPointer(e->key);
        s3::BBox4 b = bbox_to_vec(box);

        if (first)
        {
            acc = b;
            first = false;
        }
        else
        {
            acc = s3::bbox_union(acc, b);
        }
    }

    S3GistBBox* out = bbox_from_vec(acc);
    *sizep = VARSIZE(out);
    PG_RETURN_POINTER(out);
}

PG_FUNCTION_INFO_V1(gist_s3_penalty);
Datum gist_s3_penalty(PG_FUNCTION_ARGS)
{
    GISTENTRY* orig = (GISTENTRY*) PG_GETARG_POINTER(0);
    GISTENTRY* add = (GISTENTRY*) PG_GETARG_POINTER(1);
    float* result = (float*) PG_GETARG_POINTER(2);

    S3GistBBox* box_orig = (S3GistBBox*) DatumGetPointer(orig->key);
    S3GistBBox* box_add  = (S3GistBBox*) DatumGetPointer(add->key);

    s3::BBox4 b1 = bbox_to_vec(box_orig);
    s3::BBox4 b2 = bbox_to_vec(box_add);

    s3::BBox4 merged = s3::bbox_union(b1, b2);

    double vol1 = 1.0;
    double volm = 1.0;
    for (int i = 0; i < 4; ++i)
    {
        vol1 *= (b1.max[i] - b1.min[i]);
        volm *= (merged.max[i] - merged.min[i]);
    }

    double penalty = volm - vol1;
    if (penalty < 0.0) penalty = 0.0;

    *result = (float) penalty;
    PG_RETURN_VOID();
}

PG_FUNCTION_INFO_V1(gist_s3_picksplit);
Datum gist_s3_picksplit(PG_FUNCTION_ARGS)
{
    GistEntryVector* entryvec = (GistEntryVector*) PG_GETARG_POINTER(0);
    GIST_SPLITVEC* v = (GIST_SPLITVEC*) PG_GETARG_POINTER(1);

    int n = entryvec->n;

    // Collect all bounding boxes and find the dimension with maximum spread
    double min_vals[4] = {1e300, 1e300, 1e300, 1e300};
    double max_vals[4] = {-1e300, -1e300, -1e300, -1e300};

    for (int i = FirstOffsetNumber; i <= n; ++i)
    {
        GISTENTRY* e = &entryvec->vector[i - 1];
        S3GistBBox* box = (S3GistBBox*) DatumGetPointer(e->key);

        for (int d = 0; d < 4; ++d)
        {
            double center = (box->min[d] + box->max[d]) / 2.0;
            if (center < min_vals[d]) min_vals[d] = center;
            if (center > max_vals[d]) max_vals[d] = center;
        }
    }

    // Find dimension with maximum spread
    int split_dim = 0;
    double max_spread = 0.0;
    for (int d = 0; d < 4; ++d)
    {
        double spread = max_vals[d] - min_vals[d];
        if (spread > max_spread)
        {
            max_spread = spread;
            split_dim = d;
        }
    }

    double split_val = (min_vals[split_dim] + max_vals[split_dim]) / 2.0;

    // Allocate split vectors
    v->spl_left = (OffsetNumber*) palloc(n * sizeof(OffsetNumber));
    v->spl_right = (OffsetNumber*) palloc(n * sizeof(OffsetNumber));
    v->spl_nleft = 0;
    v->spl_nright = 0;

    s3::BBox4 left_union, right_union;
    bool left_first = true, right_first = true;

    for (int i = FirstOffsetNumber; i <= n; ++i)
    {
        GISTENTRY* e = &entryvec->vector[i - 1];
        S3GistBBox* box = (S3GistBBox*) DatumGetPointer(e->key);
        s3::BBox4 bb = bbox_to_vec(box);

        double center = (box->min[split_dim] + box->max[split_dim]) / 2.0;

        if (center < split_val)
        {
            v->spl_left[v->spl_nleft++] = i;
            if (left_first)
            {
                left_union = bb;
                left_first = false;
            }
            else
            {
                left_union = s3::bbox_union(left_union, bb);
            }
        }
        else
        {
            v->spl_right[v->spl_nright++] = i;
            if (right_first)
            {
                right_union = bb;
                right_first = false;
            }
            else
            {
                right_union = s3::bbox_union(right_union, bb);
            }
        }
    }

    // Handle edge case: all entries on one side
    if (v->spl_nleft == 0)
    {
        v->spl_left[v->spl_nleft++] = v->spl_right[--v->spl_nright];
        left_union = bbox_to_vec((S3GistBBox*) DatumGetPointer(
            entryvec->vector[v->spl_left[0] - 1].key));
    }
    if (v->spl_nright == 0)
    {
        v->spl_right[v->spl_nright++] = v->spl_left[--v->spl_nleft];
        right_union = bbox_to_vec((S3GistBBox*) DatumGetPointer(
            entryvec->vector[v->spl_right[0] - 1].key));
    }

    v->spl_ldatum = PointerGetDatum(bbox_from_vec(left_union));
    v->spl_rdatum = PointerGetDatum(bbox_from_vec(right_union));

    PG_RETURN_POINTER(v);
}

PG_FUNCTION_INFO_V1(gist_s3_same);
Datum gist_s3_same(PG_FUNCTION_ARGS)
{
    S3GistBBox* b1 = (S3GistBBox*) DatumGetPointer(PG_GETARG_DATUM(0));
    S3GistBBox* b2 = (S3GistBBox*) DatumGetPointer(PG_GETARG_DATUM(1));
    bool* result = (bool*) PG_GETARG_POINTER(2);

    for (int i = 0; i < 4; ++i)
    {
        if (b1->min[i] != b2->min[i] || b1->max[i] != b2->max[i])
        {
            *result = false;
            PG_RETURN_VOID();
        }
    }

    *result = true;
    PG_RETURN_VOID();
}

PG_FUNCTION_INFO_V1(gist_s3_distance);
Datum gist_s3_distance(PG_FUNCTION_ARGS)
{
    GISTENTRY* entry = (GISTENTRY*) PG_GETARG_POINTER(0);
    Datum query = PG_GETARG_DATUM(1);
    bool* recheck = (bool*) PG_GETARG_POINTER(4);

    *recheck = true;

    S3GistBBox* box = (S3GistBBox*) DatumGetPointer(entry->key);
    s3::BBox4 bb = bbox_to_vec(box);

    // Use datum_to_vec4 which properly handles detoasting
    s3::Vec4 qp = s3_pg::datum_to_vec4(query);

    double d = s3::distance_point_bbox(qp, bb);

    PG_RETURN_FLOAT8(d);
}

}
