#include "geometry/s3_bbox.hpp"
#include <cmath>

namespace s3
{
    double distance_point_bbox(const Vec4& p, const BBox4& b) noexcept
    {
        double acc = 0.0;
        for (int i = 0; i < 4; ++i)
        {
            double v = 0.0;
            if (p[i] < b.min[i]) v = b.min[i] - p[i];
            else if (p[i] > b.max[i]) v = p[i] - b.max[i];
            acc += v * v;
        }
        return std::sqrt(acc);
    }
}
