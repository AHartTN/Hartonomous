/**
 * @file voronoi_analysis.hpp
 * @brief Voronoi cell analysis on S³: semantic territory metrics
 *
 * Each composition's Voronoi cell on S³ represents its "semantic territory" —
 * the region of conceptual space it dominates before hitting a neighboring
 * concept's boundary. Cell properties reveal:
 *
 *   - Volume: How much semantic space does this concept own?
 *     Large cell → isolated/unique concept. Tiny cell → densely packed region.
 *
 *   - Boundary neighbors: Which concepts share borders?
 *     Shared Voronoi boundaries = conceptual adjacency regardless of graph edges.
 *
 *   - Eccentricity: How elongated is the cell?
 *     Round cells → concept is equidistant from all neighbors.
 *     Elongated → concept bridges between two clusters.
 *
 *   - Overlap analysis (firefly jar): How do different models' Voronoi
 *     partitions compare? Disagreement between models' cell assignments
 *     reveals polysemy and conceptual ambiguity.
 *
 * Implementation uses Monte Carlo sampling on S³ rather than exact geometric
 * Voronoi construction (which is expensive in 4D). For each concept, we sample
 * random points in its neighborhood and classify them by nearest concept.
 * This gives approximate but practical cell metrics.
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <export.hpp>
#include <Eigen/Dense>
#include <vector>
#include <string>
#include <unordered_map>
#include <random>

namespace Hartonomous {

struct VoronoiCell {
    BLAKE3Pipeline::Hash composition_id;
    std::string text;
    Eigen::Vector4d centroid;

    // Cell metrics
    double approximate_volume;     // Fraction of S³ surface owned (0-1)
    double avg_boundary_distance;  // Mean geodesic distance to cell boundary
    double eccentricity;           // 0=perfectly round, 1=maximally elongated

    // Boundary neighbors (shared Voronoi edges)
    struct BoundaryNeighbor {
        BLAKE3Pipeline::Hash id;
        std::string text;
        double boundary_distance;  // Geodesic distance to boundary midpoint
        double boundary_fraction;  // Fraction of boundary shared with this neighbor
    };
    std::vector<BoundaryNeighbor> boundary_neighbors;
};

struct VoronoiOverlap {
    BLAKE3Pipeline::Hash composition_id;
    std::string text;

    // Per-model cell assignments
    struct ModelCell {
        BLAKE3Pipeline::Hash content_id;  // Model source
        double volume;
        Eigen::Vector4d centroid;
    };
    std::vector<ModelCell> model_cells;

    // Disagreement metrics
    double centroid_spread;        // Average geodesic distance between model centroids
    double volume_variance;        // How much models disagree on concept's territory size
    double max_centroid_distance;  // Maximum disagreement between any two models
};

struct VoronoiConfig {
    size_t samples_per_cell = 1000;   // Monte Carlo samples per cell
    size_t max_neighbors = 32;        // Max boundary neighbors to track
    double search_radius = 0.5;       // Geodesic radius around centroid to sample
    size_t target_compositions = 0;   // 0 = analyze all; >0 = top-N by relation count
};

class HARTONOMOUS_API VoronoiAnalysis {
public:
    explicit VoronoiAnalysis(PostgresConnection& db);

    /**
     * @brief Compute Voronoi cell metrics for a single composition
     */
    VoronoiCell analyze_cell(const BLAKE3Pipeline::Hash& composition_id,
                             const VoronoiConfig& config = {});

    /**
     * @brief Compute Voronoi cells for a neighborhood of compositions
     *
     * More efficient than calling analyze_cell repeatedly — builds a shared
     * spatial index for the neighborhood.
     */
    std::vector<VoronoiCell> analyze_neighborhood(
        const BLAKE3Pipeline::Hash& center_id,
        double radius,
        const VoronoiConfig& config = {});

    /**
     * @brief Firefly jar overlap analysis
     *
     * Compares Voronoi cells across different model projections.
     * Requires model_projection table to be populated.
     */
    std::vector<VoronoiOverlap> analyze_model_overlap(
        const std::vector<BLAKE3Pipeline::Hash>& composition_ids,
        const VoronoiConfig& config = {});

    /**
     * @brief Find polysemous concepts — those with high disagreement
     *        across model projections
     */
    std::vector<VoronoiOverlap> find_polysemous(
        double min_spread = 0.3,
        size_t limit = 50);

    /**
     * @brief Find boundary concepts — those equidistant from multiple clusters
     *
     * These are the "interesting" concepts for reasoning: words with multiple
     * meanings, concepts that bridge domains.
     */
    std::vector<VoronoiCell> find_boundary_concepts(
        double min_neighbor_count = 8,
        double max_eccentricity = 0.3,
        size_t limit = 50);

private:
    // Generate random point on S³ near a given centroid within geodesic radius
    Eigen::Vector4d sample_near(const Eigen::Vector4d& center,
                                double radius, std::mt19937& rng) const;

    // Geodesic distance on S³
    double geodesic(const Eigen::Vector4d& a, const Eigen::Vector4d& b) const;

    // Load positions for compositions within radius of center
    struct PositionEntry {
        BLAKE3Pipeline::Hash id;
        std::string text;
        Eigen::Vector4d position;
    };
    std::vector<PositionEntry> load_neighborhood(
        const Eigen::Vector4d& center, double radius);

    // Find nearest composition to a point (brute force within neighborhood)
    const PositionEntry* find_nearest(
        const Eigen::Vector4d& point,
        const std::vector<PositionEntry>& neighborhood) const;

    PostgresConnection& db_;
};

} // namespace Hartonomous
