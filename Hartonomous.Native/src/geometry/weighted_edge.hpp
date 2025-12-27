#pragma once

#include "../atoms/atom_id.hpp"
#include "point_zm.hpp"
#include "../atoms/semantic_hilbert.hpp"
#include <cstdio>

namespace hartonomous {

/// Weighted edge between two atoms
/// Can represent: transition probability, attention weight, embedding dimension
struct WeightedEdge {
    AtomId from;
    AtomId to;
    double weight;

    constexpr WeightedEdge() noexcept : from{}, to{}, weight(0.0) {}
    constexpr WeightedEdge(AtomId f, AtomId t, double w) noexcept
        : from(f), to(t), weight(w) {}

    /// Format as WKT LINEZM where M encodes the weight
    void to_wkt(char* buffer, std::size_t size) const noexcept {
        auto p1 = PointZM::from_semantic(SemanticHilbert::to_semantic(from));
        auto p2 = PointZM::from_semantic(SemanticHilbert::to_semantic(to));

        // Use M coordinate to encode weight (scaled)
        std::snprintf(buffer, size,
            "LINESTRING ZM (%.6f %.6f %.6f %.6f, %.6f %.6f %.6f %.6f)",
            p1.x, p1.y, p1.z, 0.0,  // Start point, M=0
            p2.x, p2.y, p2.z, weight * 1000.0);  // End point, M=weight
    }
};

} // namespace hartonomous
