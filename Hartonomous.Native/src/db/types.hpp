#pragma once

/// DB TYPES - Common data structures for database queries.
///
/// These are the query result types used throughout the database layer.
/// Separated from query_store.hpp to reduce coupling and improve compile times.

#include "../atoms/node_ref.hpp"
#include <string>
#include <vector>
#include <cstdint>
#include <cstdio>

namespace hartonomous::db {

/// Query result for spatial searches.
/// Contains Hilbert coordinates, codepoint, and distance from query point.
struct SpatialMatch {
    std::int64_t hilbert_high;
    std::int64_t hilbert_low;
    std::int32_t codepoint;
    double distance;
};

/// Query result for composition lookup.
/// Contains the root reference, existence flag, and decoded byte count.
struct CompositionResult {
    NodeRef root;
    bool exists;
    std::size_t byte_count;  // decoded size
};

/// Trajectory point through semantic space - single RLE-compressed coordinate.
/// Uses 4D semantic coordinates: (page, type, base, variant).
struct TrajectoryPoint {
    std::int16_t page;      // X: Unicode page
    std::int16_t type;      // Y: Character type  
    std::int32_t base;      // Z: Base character
    std::uint8_t variant;   // M: Variant (case/diacritical)
    std::uint32_t count;    // RLE: repetition count (1 = no repeat)
};

/// A trajectory is a sequence of RLE-compressed semantic coordinates.
/// Represents a path through 4D semantic space (used for embeddings, model weights).
struct Trajectory {
    std::vector<TrajectoryPoint> points;
    double weight;  // For model weights - salience score

    /// Build WKT for LineStringZM (expanding RLE).
    /// Used for PostGIS geometry storage.
    [[nodiscard]] std::string to_wkt() const {
        if (points.empty()) return "LINESTRINGZM EMPTY";

        std::string wkt = "LINESTRINGZM(";
        bool first = true;
        for (const auto& p : points) {
            for (std::uint32_t i = 0; i < p.count; ++i) {
                if (!first) wkt += ", ";
                first = false;
                char buf[64];
                std::snprintf(buf, sizeof(buf), "%d %d %d %d",
                    p.page, p.type, p.base, p.variant);
                wkt += buf;
            }
        }
        wkt += ")";
        return wkt;
    }

    /// Total length (with RLE expansion).
    [[nodiscard]] std::size_t expanded_length() const {
        std::size_t len = 0;
        for (const auto& p : points) len += p.count;
        return len;
    }
};

/// Relationship type is DEPRECATED - relationships are universal.
/// Source/context is encoded in context_high/context_low (NodeRef of source).
/// The rel_type column remains for backward compatibility but should be 0.
using RelType = std::int16_t;
constexpr RelType REL_DEFAULT = 0;

/// Relationship result for queries (sparse - only stored relationships appear).
/// Represents a weighted edge in the semantic graph.
struct Relationship {
    NodeRef from;
    NodeRef to;
    double weight;
    std::int32_t obs_count;  // How many times this edge was observed
    std::int16_t rel_type;
    NodeRef context;
};

} // namespace hartonomous::db
