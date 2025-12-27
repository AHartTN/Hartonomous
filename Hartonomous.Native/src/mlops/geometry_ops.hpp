#pragma once

/// GEOMETRY OPERATIONS - Full exploitation of the spatial substrate
///
/// THE SUBSTRATE PROVIDES:
/// 1. POINTZM - Every atom has a 4D position (Page, Type, Base, Variant)
/// 2. LineStringZM - Every composition has a trajectory through 4D space
/// 3. Hilbert Index - 128-bit locality-preserving encoding
/// 4. GiST Index - O(log n) spatial queries in PostgreSQL
///
/// THIS FILE EXPLOITS ALL OF THEM:
/// - Hilbert range queries: Find all atoms in semantic neighborhood
/// - Trajectory similarity: ST_FrechetDistance for sequence comparison
/// - Spatial attention: Attend based on geometric proximity
/// - Convex hull containment: Does X contain Y semantically?

#include "../db/query_store.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/semantic_decompose.hpp"
#include "../atoms/codepoint_atom_table.hpp"
#include "../hilbert/hilbert_encoder.hpp"
#include <vector>
#include <cmath>
#include <algorithm>

namespace hartonomous::mlops {

using db::QueryStore;
using db::Trajectory;
using db::TrajectoryPoint;
using db::SpatialMatch;

// ============================================================================
// UTF-8 AWARE TRAJECTORY BUILDING
// ============================================================================

/// Build trajectory from text with PROPER UTF-8 decoding.
/// Each CODEPOINT (not byte) becomes a point in 4D space.
[[nodiscard]] inline Trajectory build_trajectory_utf8(const std::string& text) {
    Trajectory traj;
    if (text.empty()) return traj;

    auto codepoints = UTF8Decoder::decode(text);
    if (codepoints.empty()) return traj;

    TrajectoryPoint current{};
    bool has_current = false;

    for (std::int32_t cp : codepoints) {
        auto coord = SemanticDecompose::get_coord(cp);

        if (has_current &&
            current.page == coord.page &&
            current.type == coord.type &&
            current.base == coord.base &&
            current.variant == coord.variant) {
            // Same point - increment RLE count
            current.count++;
        } else {
            // New point
            if (has_current) {
                traj.points.push_back(current);
            }
            current.page = static_cast<std::int16_t>(coord.page);
            current.type = static_cast<std::int16_t>(coord.type);
            current.base = coord.base;
            current.variant = coord.variant;
            current.count = 1;
            has_current = true;
        }
    }

    if (has_current) {
        traj.points.push_back(current);
    }

    return traj;
}

// ============================================================================
// HILBERT RANGE QUERIES
// ============================================================================

/// Hilbert Range - represents a range of semantically similar atoms
struct HilbertRange {
    AtomId start;  // Inclusive
    AtomId end;    // Inclusive
    std::size_t estimated_atoms;

    /// Check if an AtomId is within this range
    [[nodiscard]] bool contains(AtomId id) const {
        // Lexicographic comparison on (high, low)
        if (id.high < start.high || id.high > end.high) return false;
        if (id.high == start.high && id.low < start.low) return false;
        if (id.high == end.high && id.low > end.low) return false;
        return true;
    }
};

/// Compute Hilbert range for "atoms similar to X"
/// Uses the locality property: nearby in 4D → nearby in Hilbert index
[[nodiscard]] inline HilbertRange compute_similar_range(
    std::int32_t codepoint,
    std::uint32_t radius = 10)
{
    auto coord = SemanticDecompose::get_coord(codepoint);

    // Compute bounding box in 4D
    std::uint32_t x_min = coord.page > radius ? coord.page - radius : 0;
    std::uint32_t x_max = coord.page + radius;
    std::uint32_t y_min = coord.type > radius ? coord.type - radius : 0;
    std::uint32_t y_max = coord.type + radius;
    std::uint32_t z_min = coord.base > static_cast<std::int32_t>(radius) ? coord.base - radius : 0;
    std::uint32_t z_max = coord.base + radius;
    std::uint32_t w_min = coord.variant > radius ? coord.variant - radius : 0;
    std::uint32_t w_max = coord.variant + radius;

    // Compute Hilbert indices at corners (approximation)
    // True Hilbert range would require curve walking, but corners give bounds
    AtomId corner1 = HilbertEncoder::encode(x_min, y_min, z_min, w_min);
    AtomId corner2 = HilbertEncoder::encode(x_max, y_max, z_max, w_max);

    HilbertRange range;
    // Order by lexicographic comparison
    if (corner1.high < corner2.high ||
        (corner1.high == corner2.high && corner1.low < corner2.low)) {
        range.start = corner1;
        range.end = corner2;
    } else {
        range.start = corner2;
        range.end = corner1;
    }

    // Estimate atom count (volume of 4D box)
    range.estimated_atoms = static_cast<std::size_t>(
        (x_max - x_min + 1) * (y_max - y_min + 1) *
        (z_max - z_min + 1) * (w_max - w_min + 1));

    return range;
}

// ============================================================================
// TRAJECTORY SIMILARITY - Frechet Distance
// ============================================================================

/// Compute Frechet distance between two trajectories (simplified).
/// True Frechet is ST_FrechetDistance in PostGIS - this is for in-memory comparison.
[[nodiscard]] inline double frechet_distance(
    const Trajectory& a, const Trajectory& b)
{
    if (a.points.empty() || b.points.empty()) {
        return std::numeric_limits<double>::infinity();
    }

    // Expand RLE for comparison
    auto expand = [](const Trajectory& t) -> std::vector<std::array<double, 4>> {
        std::vector<std::array<double, 4>> points;
        for (const auto& p : t.points) {
            for (std::uint32_t i = 0; i < p.count; ++i) {
                points.push_back({
                    static_cast<double>(p.page),
                    static_cast<double>(p.type),
                    static_cast<double>(p.base),
                    static_cast<double>(p.variant)
                });
            }
        }
        return points;
    };

    auto pa = expand(a);
    auto pb = expand(b);

    if (pa.empty() || pb.empty()) {
        return std::numeric_limits<double>::infinity();
    }

    // Point distance in 4D
    auto dist = [](const std::array<double, 4>& x, const std::array<double, 4>& y) -> double {
        double sum = 0;
        for (int i = 0; i < 4; ++i) {
            double d = x[i] - y[i];
            sum += d * d;
        }
        return std::sqrt(sum);
    };

    // Dynamic programming Frechet distance
    std::size_t n = pa.size();
    std::size_t m = pb.size();

    // For large trajectories, use simplified version
    if (n * m > 1000000) {
        // Hausdorff-like approximation
        double max_dist = 0;
        std::size_t step = std::max(std::size_t(1), std::max(n, m) / 100);

        for (std::size_t i = 0; i < n; i += step) {
            double min_d = std::numeric_limits<double>::infinity();
            for (std::size_t j = 0; j < m; j += step) {
                min_d = std::min(min_d, dist(pa[i], pb[j]));
            }
            max_dist = std::max(max_dist, min_d);
        }
        return max_dist;
    }

    // Full DP for smaller trajectories
    std::vector<std::vector<double>> dp(n, std::vector<double>(m));

    dp[0][0] = dist(pa[0], pb[0]);
    for (std::size_t i = 1; i < n; ++i) {
        dp[i][0] = std::max(dp[i-1][0], dist(pa[i], pb[0]));
    }
    for (std::size_t j = 1; j < m; ++j) {
        dp[0][j] = std::max(dp[0][j-1], dist(pa[0], pb[j]));
    }

    for (std::size_t i = 1; i < n; ++i) {
        for (std::size_t j = 1; j < m; ++j) {
            double d = dist(pa[i], pb[j]);
            dp[i][j] = std::max(d, std::min({dp[i-1][j], dp[i][j-1], dp[i-1][j-1]}));
        }
    }

    return dp[n-1][m-1];
}

/// Compute cosine similarity between two trajectories (treating as vectors).
[[nodiscard]] inline double trajectory_cosine_similarity(
    const Trajectory& a, const Trajectory& b)
{
    if (a.points.empty() || b.points.empty()) return 0.0;

    // Compute centroid for each trajectory
    auto centroid = [](const Trajectory& t) -> std::array<double, 4> {
        std::array<double, 4> sum = {0, 0, 0, 0};
        std::size_t total = 0;
        for (const auto& p : t.points) {
            sum[0] += p.page * p.count;
            sum[1] += p.type * p.count;
            sum[2] += p.base * p.count;
            sum[3] += p.variant * p.count;
            total += p.count;
        }
        if (total > 0) {
            for (auto& s : sum) s /= static_cast<double>(total);
        }
        return sum;
    };

    auto ca = centroid(a);
    auto cb = centroid(b);

    // Cosine similarity of centroids
    double dot = 0, norm_a = 0, norm_b = 0;
    for (int i = 0; i < 4; ++i) {
        dot += ca[i] * cb[i];
        norm_a += ca[i] * ca[i];
        norm_b += cb[i] * cb[i];
    }

    if (norm_a < 1e-10 || norm_b < 1e-10) return 0.0;
    return dot / (std::sqrt(norm_a) * std::sqrt(norm_b));
}

// ============================================================================
// SPATIAL ATTENTION - Attend based on geometry
// ============================================================================

/// Spatial attention scores - weight by inverse distance in semantic space
struct SpatialAttentionResult {
    struct AttendedAtom {
        std::int32_t codepoint;
        NodeRef ref;
        double distance;
        double attention_weight;
    };
    std::vector<AttendedAtom> attended;
};

/// Compute spatial attention from a query codepoint.
/// Returns atoms weighted by inverse semantic distance.
[[nodiscard]] inline SpatialAttentionResult spatial_attention(
    QueryStore& store,
    std::int32_t query_codepoint,
    std::size_t top_k = 20,
    double temperature = 1.0)
{
    SpatialAttentionResult result;

    // Find spatially similar atoms
    auto similar = store.find_similar(query_codepoint, top_k * 2);

    if (similar.empty()) return result;

    // Compute softmax over inverse distances
    std::vector<double> scores;
    scores.reserve(similar.size());

    double max_score = -std::numeric_limits<double>::infinity();
    for (const auto& match : similar) {
        double score = -match.distance / temperature;  // Inverse distance
        scores.push_back(score);
        max_score = std::max(max_score, score);
    }

    // Softmax normalization
    double sum_exp = 0.0;
    for (auto& s : scores) {
        s = std::exp(s - max_score);  // Subtract max for numerical stability
        sum_exp += s;
    }

    result.attended.reserve(std::min(top_k, similar.size()));
    for (std::size_t i = 0; i < std::min(top_k, similar.size()); ++i) {
        SpatialAttentionResult::AttendedAtom atom;
        atom.codepoint = similar[i].codepoint;
        atom.ref = NodeRef::atom(similar[i].hilbert_high, similar[i].hilbert_low);
        atom.distance = similar[i].distance;
        atom.attention_weight = scores[i] / sum_exp;
        result.attended.push_back(atom);
    }

    // Sort by attention weight
    std::sort(result.attended.begin(), result.attended.end(),
        [](const auto& a, const auto& b) { return a.attention_weight > b.attention_weight; });

    return result;
}

// ============================================================================
// CONVEX HULL CONTAINMENT
// ============================================================================

/// Check if trajectory B is semantically contained within trajectory A.
/// Uses 4D convex hull approximation (bounding hypercube).
[[nodiscard]] inline bool trajectory_contains(const Trajectory& container, const Trajectory& contained) {
    if (container.points.empty() || contained.points.empty()) return false;

    // Compute bounding boxes
    auto bbox = [](const Trajectory& t) -> std::tuple<int32_t, int32_t, int32_t, int32_t,
                                                       int32_t, int32_t, int32_t, int32_t> {
        int32_t min_p = INT32_MAX, max_p = INT32_MIN;
        int32_t min_t = INT32_MAX, max_t = INT32_MIN;
        int32_t min_b = INT32_MAX, max_b = INT32_MIN;
        int32_t min_v = INT32_MAX, max_v = INT32_MIN;

        for (const auto& pt : t.points) {
            min_p = std::min(min_p, static_cast<int32_t>(pt.page));
            max_p = std::max(max_p, static_cast<int32_t>(pt.page));
            min_t = std::min(min_t, static_cast<int32_t>(pt.type));
            max_t = std::max(max_t, static_cast<int32_t>(pt.type));
            min_b = std::min(min_b, pt.base);
            max_b = std::max(max_b, pt.base);
            min_v = std::min(min_v, static_cast<int32_t>(pt.variant));
            max_v = std::max(max_v, static_cast<int32_t>(pt.variant));
        }

        return {min_p, max_p, min_t, max_t, min_b, max_b, min_v, max_v};
    };

    auto [a_min_p, a_max_p, a_min_t, a_max_t, a_min_b, a_max_b, a_min_v, a_max_v] = bbox(container);
    auto [b_min_p, b_max_p, b_min_t, b_max_t, b_min_b, b_max_b, b_min_v, b_max_v] = bbox(contained);

    // Check if B's bounding box is inside A's
    return b_min_p >= a_min_p && b_max_p <= a_max_p &&
           b_min_t >= a_min_t && b_max_t <= a_max_t &&
           b_min_b >= a_min_b && b_max_b <= a_max_b &&
           b_min_v >= a_min_v && b_max_v <= a_max_v;
}

// ============================================================================
// SEMANTIC DISTANCE METRICS
// ============================================================================

/// Compute semantic distance between two codepoints.
/// Uses the 4D Euclidean distance in (Page, Type, Base, Variant) space.
[[nodiscard]] inline double semantic_distance(std::int32_t cp_a, std::int32_t cp_b) {
    auto a = SemanticDecompose::get_coord(cp_a);
    auto b = SemanticDecompose::get_coord(cp_b);

    double dx = static_cast<double>(a.page) - static_cast<double>(b.page);
    double dy = static_cast<double>(a.type) - static_cast<double>(b.type);
    double dz = static_cast<double>(a.base) - static_cast<double>(b.base);
    double dw = static_cast<double>(a.variant) - static_cast<double>(b.variant);

    return std::sqrt(dx*dx + dy*dy + dz*dz + dw*dw);
}

/// Weighted semantic distance (different dimensions have different importance).
[[nodiscard]] inline double weighted_semantic_distance(
    std::int32_t cp_a, std::int32_t cp_b,
    double w_page = 100.0,    // Script differences are large
    double w_type = 50.0,     // Type differences are medium
    double w_base = 1.0,      // Base differences are fine-grained
    double w_variant = 0.1)   // Variant differences are small
{
    auto a = SemanticDecompose::get_coord(cp_a);
    auto b = SemanticDecompose::get_coord(cp_b);

    double dx = static_cast<double>(a.page) - static_cast<double>(b.page);
    double dy = static_cast<double>(a.type) - static_cast<double>(b.type);
    double dz = static_cast<double>(a.base) - static_cast<double>(b.base);
    double dw = static_cast<double>(a.variant) - static_cast<double>(b.variant);

    return std::sqrt(w_page*dx*dx + w_type*dy*dy + w_base*dz*dz + w_variant*dw*dw);
}

// ============================================================================
// BATCH GEOMETRY OPERATIONS
// ============================================================================

/// Find all case-equivalent strings for a text.
/// Uses the variant dimension: same (Page, Type, Base) but different Variant.
[[nodiscard]] inline std::vector<std::string> case_equivalents(const std::string& text) {
    auto codepoints = UTF8Decoder::decode(text);
    if (codepoints.empty()) return {text};

    // For each codepoint, get all variant forms
    std::vector<std::vector<std::int32_t>> variants_per_cp;
    variants_per_cp.reserve(codepoints.size());

    for (std::int32_t cp : codepoints) {
        auto coord = SemanticDecompose::get_coord(cp);
        std::vector<std::int32_t> variants;

        // Try all variant values (0-15 covers most cases)
        for (std::uint8_t v = 0; v < 16; ++v) {
            SemanticCoord test{coord.page, coord.type, coord.base, v};
            std::int32_t test_cp = SemanticDecompose::to_codepoint(test);
            if (test_cp != coord.base || v == 0) {  // Valid variant
                variants.push_back(test_cp);
            }
        }

        if (variants.empty()) {
            variants.push_back(cp);  // Keep original if no variants
        }
        variants_per_cp.push_back(std::move(variants));
    }

    // Enumerate combinations (limit to avoid explosion)
    std::vector<std::string> results;
    results.reserve(16);  // Common case: 2^4 combinations

    std::function<void(std::size_t, std::vector<std::int32_t>&)> enumerate =
        [&](std::size_t idx, std::vector<std::int32_t>& current) {
            if (results.size() >= 256) return;  // Limit explosion

            if (idx == variants_per_cp.size()) {
                results.push_back(UTF8Decoder::encode(current));
                return;
            }

            for (std::int32_t cp : variants_per_cp[idx]) {
                current.push_back(cp);
                enumerate(idx + 1, current);
                current.pop_back();
            }
        };

    std::vector<std::int32_t> current;
    enumerate(0, current);

    return results;
}

} // namespace hartonomous::mlops
