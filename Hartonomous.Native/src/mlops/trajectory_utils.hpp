#pragma once

/// TRAJECTORY UTILITIES - Common trajectory operations for MLOps.
///
/// Provides reusable trajectory distance and similarity calculations.
/// Extracted to eliminate duplication across AttentionOp, GenerationOp, etc.

#include "../db/query_store.hpp"
#include <cmath>
#include <algorithm>
#include <limits>

namespace hartonomous::mlops {

using db::Trajectory;
using db::TrajectoryPoint;

/// Compute simplified trajectory distance (point-wise Euclidean sum).
/// Uses Hausdorff-like distance between two trajectories.
///
/// @param a First trajectory
/// @param b Second trajectory
/// @return Euclidean distance sum (0 = identical, larger = more different)
[[nodiscard]] inline double trajectory_distance(
    const Trajectory& a, const Trajectory& b) noexcept
{
    if (a.points.empty() || b.points.empty()) {
        return std::numeric_limits<double>::max();
    }

    // Sum squared distances between corresponding points
    // Pad shorter trajectory with last point
    std::size_t len = std::max(a.points.size(), b.points.size());
    double sum_sq = 0.0;

    for (std::size_t i = 0; i < len; ++i) {
        const auto& pa = a.points[std::min(i, a.points.size() - 1)];
        const auto& pb = b.points[std::min(i, b.points.size() - 1)];

        double dx = static_cast<double>(pa.page - pb.page);
        double dy = static_cast<double>(pa.type - pb.type);
        double dz = static_cast<double>(pa.base - pb.base);
        double dm = static_cast<double>(pa.variant - pb.variant);

        sum_sq += dx*dx + dy*dy + dz*dz + dm*dm;
    }

    return std::sqrt(sum_sq);
}

/// Convert trajectory distance to similarity score.
/// similarity = 1 / (1 + distance) -> range [0, 1]
///
/// @param distance Distance between trajectories
/// @return Similarity score (1 = identical, 0 = very different)
[[nodiscard]] inline double distance_to_similarity(double distance) noexcept {
    return 1.0 / (1.0 + distance);
}

/// Convert trajectory distance to attention score.
/// Uses inverse distance with configurable scale.
///
/// @param distance Distance between trajectories
/// @param scale Scale factor for attention (default 10.0)
/// @return Attention score (higher = more attention)
[[nodiscard]] inline double distance_to_attention(
    double distance, double scale = 10.0) noexcept
{
    return scale / (distance + 1.0);
}

} // namespace hartonomous::mlops
