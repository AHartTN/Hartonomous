#include <unicode/ingestor/node_generator.hpp>
#include <geometry/hopf_fibration.hpp>
#include <cmath>

namespace Hartonomous::unicode {

// Golden ratio and Plastic constant — irrational bases that produce
// maximally uniform angular distributions without periodic repetition
static constexpr double PHI = 1.61803398874989484820;
static constexpr double PSI = 1.32471795724474602596;

NodeGenerator::Vec4 NodeGenerator::generate_node(size_t i, size_t N) {
    if (N == 0) return Vec4::Zero();

    // Normalized index with midpoint rule (avoids poles)
    double t = (static_cast<double>(i) + 0.5) / static_cast<double>(N);

    // Fibonacci lattice on S²: golden angle drives longitude
    double s2_y = 1.0 - 2.0 * t;
    double radius = std::sqrt(std::max(0.0, 1.0 - s2_y * s2_y));
    double theta_s2 = 2.0 * M_PI * t * PHI;

    double s2_x = radius * std::cos(theta_s2);
    double s2_z = radius * std::sin(theta_s2);

    // Fiber phase: Plastic constant decouples from golden ratio
    double fiber_angle = 2.0 * M_PI * t * PSI;

    // Hopf inverse: S² × S¹ → S³
    hartonomous::geometry::HopfFibration::Vec3 s2_point(s2_x, s2_y, s2_z);
    return hartonomous::geometry::HopfFibration::inverse(s2_point, fiber_angle);
}

} // namespace Hartonomous::unicode
