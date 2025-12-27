#pragma once

#include "point_zm.hpp"
#include <cstdint>
#include <array>
#include <cmath>
#include <cstdio>

namespace hartonomous {

/// A sequence of points forming a path through semantic space.
/// Represents compositions (words, phrases, documents) as trajectories.
///
/// Compatible with PostGIS LINESTRINGZM.
template<std::size_t MaxPoints = 256>
class LineStringZM {
public:
    std::array<PointZM, MaxPoints> points;
    std::size_t count;

    constexpr LineStringZM() noexcept : points{}, count(0) {}

    /// Add a point to the linestring
    constexpr bool push_back(PointZM pt) noexcept {
        if (count >= MaxPoints) return false;
        points[count++] = pt;
        return true;
    }

    /// Add a codepoint as a point
    constexpr bool push_codepoint(std::int32_t codepoint) noexcept {
        return push_back(PointZM::from_codepoint(codepoint));
    }

    /// Total path length (sum of segment lengths)
    [[nodiscard]] double length() const noexcept {
        if (count < 2) return 0.0;
        double total = 0.0;
        for (std::size_t i = 1; i < count; ++i) {
            total += std::sqrt(points[i-1].distance_squared(points[i]));
        }
        return total;
    }

    /// Discrete Fréchet distance to another linestring
    /// Measures similarity of two paths (order-sensitive)
    /// O(n*m) algorithm - for large paths, use approximation
    [[nodiscard]] double frechet_distance(const LineStringZM& other) const noexcept {
        if (count == 0 || other.count == 0) return 0.0;

        // Simple recursive Fréchet (for small paths)
        // Production would use dynamic programming
        return frechet_recursive(0, 0, other);
    }

    /// Format as WKT for PostGIS
    void to_wkt(char* buffer, std::size_t size) const noexcept {
        if (count == 0) {
            std::snprintf(buffer, size, "LINESTRING ZM EMPTY");
            return;
        }

        std::size_t pos = 0;
        pos += std::snprintf(buffer + pos, size - pos, "LINESTRING ZM (");

        for (std::size_t i = 0; i < count && pos < size - 50; ++i) {
            if (i > 0) pos += std::snprintf(buffer + pos, size - pos, ", ");
            pos += std::snprintf(buffer + pos, size - pos,
                "%.6f %.6f %.6f %.6f",
                points[i].x, points[i].y, points[i].z, points[i].m);
        }

        std::snprintf(buffer + pos, size - pos, ")");
    }

private:
    [[nodiscard]] double frechet_recursive(
        std::size_t i, std::size_t j, const LineStringZM& other) const noexcept
    {
        double d = std::sqrt(points[i].distance_squared(other.points[j]));

        if (i == count - 1 && j == other.count - 1) {
            return d;
        }

        double min_rest = 1e300;  // Infinity
        if (i < count - 1 && j < other.count - 1) {
            min_rest = std::min(min_rest, frechet_recursive(i+1, j+1, other));
        }
        if (i < count - 1) {
            min_rest = std::min(min_rest, frechet_recursive(i+1, j, other));
        }
        if (j < other.count - 1) {
            min_rest = std::min(min_rest, frechet_recursive(i, j+1, other));
        }

        return std::max(d, min_rest);
    }
};

/// Create a LineStringZM from a null-terminated string
template<std::size_t MaxPoints = 256>
[[nodiscard]] LineStringZM<MaxPoints> string_to_linestring(const char* str) noexcept {
    LineStringZM<MaxPoints> result;

    // Simple ASCII handling - UTF-8 decoding would go here for full support
    for (const char* p = str; *p && result.count < MaxPoints; ++p) {
        result.push_codepoint(static_cast<std::int32_t>(*p));
    }

    return result;
}

} // namespace hartonomous
