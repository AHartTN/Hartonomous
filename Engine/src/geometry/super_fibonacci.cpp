#include "../../include/geometry/super_fibonacci.hpp"
#include "../../include/geometry/hopf_fibration.hpp"

#include <cmath>
#include <cstring>
#include <limits>

namespace hartonomous::geometry {

// Constants
const double SuperFibonacci::PHI = 1.61803398874989484820; // (1 + sqrt(5)) / 2
const double SuperFibonacci::PSI = 1.32471795724474602596; // Plastic Constant

SuperFibonacci::Vec4 SuperFibonacci::point_on_s3(size_t i, size_t N) {
    if (N == 0) return Vec4::Zero();

    // 1. Calculate normalized index t \in (0, 1)
    // Using (i + 0.5) applies the midpoint rule, avoiding poles and 
    // improving integration error convergence.
    double t = (static_cast<double>(i) + 0.5) / static_cast<double>(N);

    // 2. Generate point on Base Sphere S² (Fibonacci Lattice)
    // We use a cylindrical projection:
    // y is linearly distributed from -1 to 1 (cos(theta))
    double s2_y = 1.0 - 2.0 * t; 
    
    // Radius at this height (sin(theta))
    // Use max(0.0) to prevent NaN from tiny floating point errors near poles
    double radius = std::sqrt(std::max(0.0, 1.0 - s2_y * s2_y));

    // Longitude on S² (Golden Angle increment)
    double theta_s2 = 2.0 * M_PI * t * PHI;

    double s2_x = radius * std::cos(theta_s2);
    double s2_z = radius * std::sin(theta_s2);

    // 3. Generate Fiber Phase on S¹
    // We use the Plastic Constant (PSI) to decouple this rotation from the
    // Golden Ratio used on the base sphere.
    double fiber_angle = 2.0 * M_PI * t * PSI;

    // 4. Lift to S³ using the Hopf Inverse
    // We create the S² vector and pass it to the HopfFibration utility.
    HopfFibration::Vec3 s2_point(s2_x, s2_y, s2_z);
    
    return HopfFibration::inverse(s2_point, fiber_angle);
}

std::vector<SuperFibonacci::Vec4> SuperFibonacci::generate_points(size_t N) {
    std::vector<Vec4> points;
    points.reserve(N); // Pre-allocate for performance
    for (size_t i = 0; i < N; ++i) {
        points.push_back(point_on_s3(i, N));
    }
    return points;
}

SuperFibonacci::Vec4 SuperFibonacci::hash_to_point(const unsigned char* hash_bytes) {
    // 1. Deterministic Seed Extraction
    // Collapse 128-bit hash into a 64-bit integer index.
    // We use a mixing step to ensure all-0 and all-1 hashes don't collide.
    uint64_t part1, part2;
    std::memcpy(&part1, hash_bytes, sizeof(uint64_t));
    std::memcpy(&part2, hash_bytes + sizeof(uint64_t), sizeof(uint64_t));
    
    // FNV-style mixing or just a simple bit rotation
    uint64_t seed = part1 ^ (part2 + 0x9e3779b9 + (part1 << 6) + (part1 >> 2));

    // 2. Normalize to t \in [0, 1)
    constexpr double NORM = 1.0 / static_cast<double>(std::numeric_limits<uint64_t>::max());
    double t = static_cast<double>(seed) * NORM;

    // 3. Compute S² Coordinates
    // For single-point hashing, we compute phases directly from t.
    double s2_y = 1.0 - 2.0 * t;
    double radius = std::sqrt(std::max(0.0, 1.0 - s2_y * s2_y));

    // Use std::fmod to keep angles within [0, 2PI) to preserve precision
    // for large indices.
    // Angle = (t * Irrational) mod 1.0 * 2PI
    double theta_s2 = std::fmod(t * PHI, 1.0) * 2.0 * M_PI;
    
    double s2_x = radius * std::cos(theta_s2);
    double s2_z = radius * std::sin(theta_s2);

    // 4. Compute Fiber Phase
    double fiber_angle = std::fmod(t * PSI, 1.0) * 2.0 * M_PI;

    // 5. Lift
    HopfFibration::Vec3 s2_point(s2_x, s2_y, s2_z);
    return HopfFibration::inverse(s2_point, fiber_angle);
}

} // namespace hartonomous::geometry