#pragma once

#include "../atoms/semantic_decompose.hpp"
#include <cstdint>
#include <cstdio>

namespace hartonomous {

/// A 4D point (POINTZM) representing an atom's position in semantic space.
/// Compatible with PostGIS geometry types.
///
/// X = Page/Block (0-7 scaled to coordinate range)
/// Y = Type (0-7 scaled to coordinate range)
/// Z = Base character (codepoint scaled)
/// M = Variant (0-31 scaled to coordinate range)
///
/// All coordinates are double-precision for PostGIS compatibility,
/// but derived from integer semantic coordinates for determinism.
struct PointZM {
    double x;  // Page dimension
    double y;  // Type dimension
    double z;  // Base character dimension
    double m;  // Variant dimension (M = measure in PostGIS)

    constexpr PointZM() noexcept : x(0), y(0), z(0), m(0) {}
    constexpr PointZM(double x_, double y_, double z_, double m_) noexcept
        : x(x_), y(y_), z(z_), m(m_) {}

    /// Create from semantic coordinates
    [[nodiscard]] static constexpr PointZM from_semantic(SemanticCoord coord) noexcept {
        // Scale to [0, 1000] range for convenient PostGIS use
        // Page: 0-7 → 0-875 (125 per page)
        // Type: 0-7 → 0-875
        // Base: 0-0x10FFFF → 0-1000 (scaled)
        // Variant: 0-31 → 0-968.75

        double x = static_cast<double>(coord.page) * 125.0;
        double y = static_cast<double>(coord.type) * 125.0;
        double z = static_cast<double>(coord.base) * (1000.0 / 0x10FFFF);
        double m = static_cast<double>(coord.variant) * (1000.0 / 31.0);

        return PointZM{x, y, z, m};
    }

    /// Create from codepoint
    [[nodiscard]] static constexpr PointZM from_codepoint(std::int32_t codepoint) noexcept {
        return from_semantic(SemanticDecompose::decompose(codepoint));
    }

    /// Euclidean distance squared (faster than distance)
    [[nodiscard]] constexpr double distance_squared(const PointZM& other) const noexcept {
        double dx = x - other.x;
        double dy = y - other.y;
        double dz = z - other.z;
        double dm = m - other.m;
        return dx*dx + dy*dy + dz*dz + dm*dm;
    }

    /// Format as WKT (Well-Known Text) for PostGIS
    /// Returns: "POINT ZM (x y z m)"
    /// Note: Caller must provide buffer
    void to_wkt(char* buffer, std::size_t size) const noexcept {
        std::snprintf(buffer, size, "POINT ZM (%.6f %.6f %.6f %.6f)", x, y, z, m);
    }
};

} // namespace hartonomous
