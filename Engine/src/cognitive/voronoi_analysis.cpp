/**
 * @file voronoi_analysis.cpp
 * @brief Monte Carlo Voronoi cell analysis on S³
 *
 * Computes approximate Voronoi cell metrics by sampling random points
 * on S³ and classifying them by nearest composition centroid. This avoids
 * the computational expense of exact 4D Voronoi construction while
 * providing the metrics we actually need: volume, boundary distances,
 * eccentricity, and neighbor identification.
 */

#include <cognitive/voronoi_analysis.hpp>
#include <algorithm>
#include <numeric>
#include <cmath>
#include <iostream>

namespace Hartonomous {

VoronoiAnalysis::VoronoiAnalysis(PostgresConnection& db) : db_(db) {}

// =============================================================================
// Geodesic distance on S³
// =============================================================================

double VoronoiAnalysis::geodesic(const Eigen::Vector4d& a, const Eigen::Vector4d& b) const {
    double d = a.dot(b);
    d = std::clamp(d, -1.0, 1.0);
    return std::acos(d);
}

// =============================================================================
// Sample random point on S³ near a centroid
// =============================================================================

Eigen::Vector4d VoronoiAnalysis::sample_near(
    const Eigen::Vector4d& center, double radius, std::mt19937& rng) const
{
    // Generate random direction in tangent space of S³ at center
    std::normal_distribution<double> normal(0.0, 1.0);
    Eigen::Vector4d tangent(normal(rng), normal(rng), normal(rng), normal(rng));

    // Project tangent vector to be orthogonal to center (tangent plane of S³)
    tangent -= tangent.dot(center) * center;
    double tangent_norm = tangent.norm();
    if (tangent_norm < 1e-10) return center;
    tangent /= tangent_norm;

    // Random distance within radius (uniform on geodesic arc)
    std::uniform_real_distribution<double> dist(0.0, radius);
    double angle = dist(rng);

    // Exponential map: move along geodesic from center in direction tangent
    Eigen::Vector4d point = std::cos(angle) * center + std::sin(angle) * tangent;
    point.normalize();

    return point;
}

// =============================================================================
// Load compositions in spatial neighborhood
// =============================================================================

std::vector<VoronoiAnalysis::PositionEntry> VoronoiAnalysis::load_neighborhood(
    const Eigen::Vector4d& center, double radius)
{
    std::vector<PositionEntry> entries;

    // Use PostGIS ST_3DDistance for spatial filtering
    // Note: PostGIS operates on XYZ, M is handled separately
    // For S³, we use a bounding box approach: euclidean distance < 2*sin(radius/2)
    double euclidean_bound = 2.0 * std::sin(std::min(radius, M_PI) / 2.0);

    std::string sql = R"(
        SELECT c.id, v.reconstructed_text,
               ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid)
        FROM hartonomous.composition c
        JOIN hartonomous.physicality p ON p.id = c.physicalityid
        JOIN hartonomous.v_composition_text v ON v.composition_id = c.id
        WHERE ST_3DDistance(
            p.centroid,
            ST_SetSRID(ST_MakePointM($1, $2, $3, $4), 0)
        ) < $5
    )";

    db_.query(sql,
        {std::to_string(center[0]), std::to_string(center[1]),
         std::to_string(center[2]), std::to_string(center[3]),
         std::to_string(euclidean_bound)},
        [&](const std::vector<std::string>& row) {
            PositionEntry e;
            e.id = BLAKE3Pipeline::from_hex(row[0]);
            e.text = row[1];
            e.position = Eigen::Vector4d(
                std::stod(row[2]), std::stod(row[3]),
                std::stod(row[4]), std::stod(row[5])
            );
            entries.push_back(e);
        }
    );

    return entries;
}

// =============================================================================
// Find nearest composition to a point
// =============================================================================

const VoronoiAnalysis::PositionEntry* VoronoiAnalysis::find_nearest(
    const Eigen::Vector4d& point,
    const std::vector<PositionEntry>& neighborhood) const
{
    const PositionEntry* best = nullptr;
    double best_dist = M_PI + 1.0;

    for (const auto& entry : neighborhood) {
        double d = geodesic(point, entry.position);
        if (d < best_dist) {
            best_dist = d;
            best = &entry;
        }
    }

    return best;
}

// =============================================================================
// Analyze a single Voronoi cell
// =============================================================================

VoronoiCell VoronoiAnalysis::analyze_cell(
    const BLAKE3Pipeline::Hash& composition_id,
    const VoronoiConfig& config)
{
    VoronoiCell cell;
    cell.composition_id = composition_id;

    // Load centroid
    std::string hex = BLAKE3Pipeline::to_hex(composition_id);
    db_.query(
        "SELECT v.reconstructed_text, ST_X(p.centroid), ST_Y(p.centroid), "
        "ST_Z(p.centroid), ST_M(p.centroid) "
        "FROM hartonomous.composition c "
        "JOIN hartonomous.physicality p ON p.id = c.physicalityid "
        "JOIN hartonomous.v_composition_text v ON v.composition_id = c.id "
        "WHERE c.id = $1",
        {hex},
        [&](const std::vector<std::string>& row) {
            cell.text = row[0];
            cell.centroid = Eigen::Vector4d(
                std::stod(row[1]), std::stod(row[2]),
                std::stod(row[3]), std::stod(row[4])
            );
        }
    );

    if (cell.text.empty()) return cell;

    // Load neighborhood
    auto neighborhood = load_neighborhood(cell.centroid, config.search_radius);
    if (neighborhood.size() < 2) {
        cell.approximate_volume = 1.0; // Only concept in the area
        cell.eccentricity = 0.0;
        cell.avg_boundary_distance = config.search_radius;
        return cell;
    }

    // Monte Carlo sampling
    std::mt19937 rng(std::hash<std::string>{}(cell.text));
    size_t owned = 0;
    std::vector<double> boundary_distances;
    std::unordered_map<BLAKE3Pipeline::Hash, size_t, HashHasher> neighbor_counts;

    // Direction tracking for eccentricity
    Eigen::Vector4d owned_sum = Eigen::Vector4d::Zero();
    Eigen::Matrix4d owned_scatter = Eigen::Matrix4d::Zero();

    for (size_t s = 0; s < config.samples_per_cell; ++s) {
        Eigen::Vector4d sample = sample_near(cell.centroid, config.search_radius, rng);
        const auto* nearest = find_nearest(sample, neighborhood);

        if (nearest && nearest->id == composition_id) {
            owned++;
            Eigen::Vector4d offset = sample - cell.centroid;
            owned_sum += offset;
            owned_scatter += offset * offset.transpose();
        } else if (nearest) {
            // Sample belongs to a neighbor — this is near a boundary
            double dist_to_boundary = geodesic(cell.centroid, sample);
            boundary_distances.push_back(dist_to_boundary);
            neighbor_counts[nearest->id]++;
        }
    }

    // Compute metrics
    cell.approximate_volume = static_cast<double>(owned) / config.samples_per_cell;

    if (!boundary_distances.empty()) {
        cell.avg_boundary_distance = std::accumulate(
            boundary_distances.begin(), boundary_distances.end(), 0.0
        ) / boundary_distances.size();
    } else {
        cell.avg_boundary_distance = config.search_radius;
    }

    // Eccentricity from scatter matrix eigenvalues
    if (owned > 10) {
        Eigen::Matrix4d scatter_normalized = owned_scatter / owned;
        Eigen::Vector4d mean = owned_sum / owned;
        scatter_normalized -= mean * mean.transpose();

        Eigen::SelfAdjointEigenSolver<Eigen::Matrix4d> eigsolver(scatter_normalized);
        if (eigsolver.info() == Eigen::Success) {
            Eigen::Vector4d eigenvalues = eigsolver.eigenvalues();
            double max_ev = eigenvalues.maxCoeff();
            double min_ev = std::max(eigenvalues.minCoeff(), 1e-10);
            cell.eccentricity = 1.0 - (min_ev / max_ev);
        }
    }

    // Boundary neighbors
    std::vector<std::pair<BLAKE3Pipeline::Hash, size_t>> sorted_neighbors(
        neighbor_counts.begin(), neighbor_counts.end());
    std::sort(sorted_neighbors.begin(), sorted_neighbors.end(),
        [](const auto& a, const auto& b) { return a.second > b.second; });

    size_t total_boundary_samples = config.samples_per_cell - owned;
    for (size_t i = 0; i < sorted_neighbors.size() && i < config.max_neighbors; ++i) {
        VoronoiCell::BoundaryNeighbor bn;
        bn.id = sorted_neighbors[i].first;

        // Find text for this neighbor
        for (const auto& entry : neighborhood) {
            if (entry.id == bn.id) {
                bn.text = entry.text;
                bn.boundary_distance = geodesic(cell.centroid, entry.position);
                break;
            }
        }

        bn.boundary_fraction = total_boundary_samples > 0
            ? static_cast<double>(sorted_neighbors[i].second) / total_boundary_samples
            : 0.0;

        cell.boundary_neighbors.push_back(bn);
    }

    return cell;
}

// =============================================================================
// Analyze a neighborhood of compositions
// =============================================================================

std::vector<VoronoiCell> VoronoiAnalysis::analyze_neighborhood(
    const BLAKE3Pipeline::Hash& center_id,
    double radius,
    const VoronoiConfig& config)
{
    std::vector<VoronoiCell> cells;

    // Load center position
    Eigen::Vector4d center;
    std::string hex = BLAKE3Pipeline::to_hex(center_id);
    bool found = false;

    db_.query(
        "SELECT ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid) "
        "FROM hartonomous.composition c "
        "JOIN hartonomous.physicality p ON p.id = c.physicalityid "
        "WHERE c.id = $1",
        {hex},
        [&](const std::vector<std::string>& row) {
            center = Eigen::Vector4d(
                std::stod(row[0]), std::stod(row[1]),
                std::stod(row[2]), std::stod(row[3])
            );
            found = true;
        }
    );

    if (!found) return cells;

    // Load all compositions in the neighborhood
    auto neighborhood = load_neighborhood(center, radius);

    // Analyze each composition's cell using the shared neighborhood
    std::mt19937 rng(42);

    for (const auto& entry : neighborhood) {
        VoronoiCell cell;
        cell.composition_id = entry.id;
        cell.text = entry.text;
        cell.centroid = entry.position;

        size_t owned = 0;
        std::unordered_map<BLAKE3Pipeline::Hash, size_t, HashHasher> neighbor_counts;
        std::vector<double> boundary_distances;

        VoronoiConfig local_config = config;
        // Fewer samples per cell when analyzing many cells
        local_config.samples_per_cell = std::min(config.samples_per_cell, size_t(200));

        for (size_t s = 0; s < local_config.samples_per_cell; ++s) {
            Eigen::Vector4d sample = sample_near(entry.position, local_config.search_radius, rng);
            const auto* nearest = find_nearest(sample, neighborhood);

            if (nearest && nearest->id == entry.id) {
                owned++;
            } else if (nearest) {
                boundary_distances.push_back(geodesic(entry.position, sample));
                neighbor_counts[nearest->id]++;
            }
        }

        cell.approximate_volume = static_cast<double>(owned) / local_config.samples_per_cell;
        cell.avg_boundary_distance = boundary_distances.empty() ? local_config.search_radius :
            std::accumulate(boundary_distances.begin(), boundary_distances.end(), 0.0) /
            boundary_distances.size();
        cell.eccentricity = 0.0; // Skip expensive eccentricity for batch analysis

        // Top boundary neighbors
        std::vector<std::pair<BLAKE3Pipeline::Hash, size_t>> sorted_nb(
            neighbor_counts.begin(), neighbor_counts.end());
        std::sort(sorted_nb.begin(), sorted_nb.end(),
            [](const auto& a, const auto& b) { return a.second > b.second; });

        size_t total_boundary = local_config.samples_per_cell - owned;
        for (size_t i = 0; i < sorted_nb.size() && i < 8; ++i) {
            VoronoiCell::BoundaryNeighbor bn;
            bn.id = sorted_nb[i].first;
            for (const auto& e : neighborhood) {
                if (e.id == bn.id) { bn.text = e.text; break; }
            }
            bn.boundary_distance = geodesic(entry.position,
                [&]() -> Eigen::Vector4d {
                    for (const auto& e : neighborhood)
                        if (e.id == bn.id) return e.position;
                    return entry.position;
                }());
            bn.boundary_fraction = total_boundary > 0
                ? static_cast<double>(sorted_nb[i].second) / total_boundary : 0.0;
            cell.boundary_neighbors.push_back(bn);
        }

        cells.push_back(cell);
    }

    return cells;
}

// =============================================================================
// Firefly jar: model overlap analysis
// =============================================================================

std::vector<VoronoiOverlap> VoronoiAnalysis::analyze_model_overlap(
    const std::vector<BLAKE3Pipeline::Hash>& composition_ids,
    const VoronoiConfig& /*config*/)
{
    std::vector<VoronoiOverlap> results;

    for (const auto& comp_id : composition_ids) {
        VoronoiOverlap overlap;
        overlap.composition_id = comp_id;

        std::string hex = BLAKE3Pipeline::to_hex(comp_id);

        // Get composition text
        db_.query(
            "SELECT v.reconstructed_text FROM hartonomous.v_composition_text v "
            "WHERE v.composition_id = $1",
            {hex},
            [&](const std::vector<std::string>& row) { overlap.text = row[0]; }
        );

        // Get all model projections for this composition
        db_.query(
            "SELECT mp.contentid, ST_X(mp.position), ST_Y(mp.position), "
            "ST_Z(mp.position), ST_M(mp.position) "
            "FROM hartonomous.modelprojection mp "
            "WHERE mp.compositionid = $1",
            {hex},
            [&](const std::vector<std::string>& row) {
                VoronoiOverlap::ModelCell mc;
                mc.content_id = BLAKE3Pipeline::from_hex(row[0]);
                mc.centroid = Eigen::Vector4d(
                    std::stod(row[1]), std::stod(row[2]),
                    std::stod(row[3]), std::stod(row[4])
                );
                mc.volume = 0.0; // Would need per-model Voronoi analysis
                overlap.model_cells.push_back(mc);
            }
        );

        // Compute disagreement metrics
        if (overlap.model_cells.size() >= 2) {
            double total_dist = 0.0;
            double max_dist = 0.0;
            int pairs = 0;

            for (size_t i = 0; i < overlap.model_cells.size(); ++i) {
                for (size_t j = i + 1; j < overlap.model_cells.size(); ++j) {
                    double d = geodesic(
                        overlap.model_cells[i].centroid,
                        overlap.model_cells[j].centroid
                    );
                    total_dist += d;
                    max_dist = std::max(max_dist, d);
                    pairs++;
                }
            }

            overlap.centroid_spread = pairs > 0 ? total_dist / pairs : 0.0;
            overlap.max_centroid_distance = max_dist;

            // Volume variance (placeholder — would need per-model Voronoi)
            overlap.volume_variance = 0.0;
        } else {
            overlap.centroid_spread = 0.0;
            overlap.max_centroid_distance = 0.0;
            overlap.volume_variance = 0.0;
        }

        results.push_back(overlap);
    }

    return results;
}

// =============================================================================
// Find polysemous concepts (high model disagreement)
// =============================================================================

std::vector<VoronoiOverlap> VoronoiAnalysis::find_polysemous(
    double min_spread, size_t limit)
{
    std::vector<VoronoiOverlap> results;

    // Find compositions with multiple model projections that are spread apart
    db_.query(
        R"(WITH multi_proj AS (
            SELECT compositionid, COUNT(*) as proj_count
            FROM hartonomous.modelprojection
            GROUP BY compositionid
            HAVING COUNT(*) >= 2
        )
        SELECT mp.compositionid
        FROM multi_proj mp
        ORDER BY mp.proj_count DESC
        LIMIT $1)",
        {std::to_string(limit * 3)}, // Over-fetch since we'll filter by spread
        [&](const std::vector<std::string>& row) {
            auto comp_id = BLAKE3Pipeline::from_hex(row[0]);
            auto overlaps = analyze_model_overlap({comp_id});
            if (!overlaps.empty() && overlaps[0].centroid_spread >= min_spread) {
                results.push_back(overlaps[0]);
            }
        }
    );

    // Sort by spread (highest disagreement first)
    std::sort(results.begin(), results.end(),
        [](const VoronoiOverlap& a, const VoronoiOverlap& b) {
            return a.centroid_spread > b.centroid_spread;
        });

    if (results.size() > limit) results.resize(limit);

    return results;
}

// =============================================================================
// Find boundary concepts (many Voronoi neighbors, low eccentricity)
// =============================================================================

std::vector<VoronoiCell> VoronoiAnalysis::find_boundary_concepts(
    double min_neighbor_count, double max_eccentricity, size_t limit)
{
    std::vector<VoronoiCell> results;

    // Find compositions with many relations (likely to be boundary concepts)
    std::vector<BLAKE3Pipeline::Hash> candidates;
    db_.query(
        "SELECT rs.compositionid, COUNT(DISTINCT rs.relationid) as rel_count "
        "FROM hartonomous.relationsequence rs "
        "GROUP BY rs.compositionid "
        "HAVING COUNT(DISTINCT rs.relationid) >= $1 "
        "ORDER BY rel_count DESC "
        "LIMIT $2",
        {std::to_string(static_cast<int>(min_neighbor_count)),
         std::to_string(limit * 3)},
        [&](const std::vector<std::string>& row) {
            candidates.push_back(BLAKE3Pipeline::from_hex(row[0]));
        }
    );

    // Analyze each candidate
    VoronoiConfig config;
    config.samples_per_cell = 500; // Moderate for screening

    for (const auto& id : candidates) {
        auto cell = analyze_cell(id, config);
        if (cell.boundary_neighbors.size() >= static_cast<size_t>(min_neighbor_count) &&
            cell.eccentricity <= max_eccentricity) {
            results.push_back(cell);
        }
        if (results.size() >= limit) break;
    }

    return results;
}

} // namespace Hartonomous
