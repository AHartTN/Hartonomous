#include "geometry/s3_distance.hpp"
#include <cmath>

namespace s3
{
    double geodesic_distance(const Vec4& a, const Vec4& b) noexcept
    {
        double d = dot(a, b);
        if (d > 1.0) d = 1.0;
        else if (d < -1.0) d = -1.0;
        return std::acos(d);
    }

    double geodesic_distance_fast_core(const Vec4& a, const Vec4& b) noexcept
    {
        const double dx = a[0] - b[0];
        const double dy = a[1] - b[1];
        const double dz = a[2] - b[2];
        const double dw = a[3] - b[3];
        const double r2 = dx*dx + dy*dy + dz*dz + dw*dw;
        const double half = 0.5 * std::sqrt(r2);
        const double clamped = (half > 1.0) ? 1.0 : half;
        return 2.0 * std::asin(clamped);
    }

    double euclidean_distance(const Vec4& a, const Vec4& b) noexcept
    {
        const double dx = a[0] - b[0];
        const double dy = a[1] - b[1];
        const double dz = a[2] - b[2];
        const double dw = a[3] - b[3];
        return std::sqrt(dx*dx + dy*dy + dz*dz + dw*dw);
    }
}
