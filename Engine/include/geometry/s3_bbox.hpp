#pragma once
#include "geometry/s3_vec.hpp"
#include "interop_api.h"

namespace s3
{
    struct BBox4
    {
        Vec4 min;
        Vec4 max;
    };

    inline BBox4 bbox_from_point(const Vec4& p) noexcept
    {
        return BBox4{p, p};
    }

    inline void bbox_expand(BBox4& b, const Vec4& p) noexcept
    {
        for (int i = 0; i < 4; ++i)
        {
            if (p[i] < b.min[i]) b.min[i] = p[i];
            if (p[i] > b.max[i]) b.max[i] = p[i];
        }
    }

    inline BBox4 bbox_union(const BBox4& a, const BBox4& b) noexcept
    {
        BBox4 r = a;
        bbox_expand(r, b.min);
        bbox_expand(r, b.max);
        return r;
    }

    HARTONOMOUS_API double distance_point_bbox(const Vec4& p, const BBox4& b) noexcept;
}
