#include <geometry/super_fibonacci.hpp>
#include <cmath> // For M_PI, std::sqrt, std::acos, std::cos, std::sin

namespace hartonomous::geometry {

// Definitions of static const members
const double SuperFibonacci::PHI = (1.0 + std::sqrt(5.0)) / 2.0;
const double SuperFibonacci::PSI = 1.324717957244746;

SuperFibonacci::Vec4 SuperFibonacci::point_on_s3(size_t i, size_t N) {
    if (N == 0) return SuperFibonacci::Vec4::Zero();
    if (N == 1) return SuperFibonacci::Vec4(1.0, 0.0, 0.0, 0.0); // A single point on S3

    // Use normalized index for distribution
    double t = static_cast<double>(i) / static_cast<double>(N);

    // Three angles for S3 (polar, azimuthal, hyper-azimuthal)
    // Leveraging the Plastic Constant (PSI) for quasi-uniform distribution
    double phi = 2.0 * M_PI * t * PSI; // Primary angle, leveraging plastic constant
    double chi = std::acos(1.0 - 2.0 * t); // Corresponds to polar angle

    SuperFibonacci::Vec4 point;
    point(0) = std::cos(chi); // W-coordinate in some conventions
    double sin_chi = std::sin(chi);
    point(1) = sin_chi * std::cos(phi);
    point(2) = sin_chi * std::sin(phi) * std::cos(phi * PHI); // Use PHI for another "twist"
    point(3) = sin_chi * std::sin(phi) * std::sin(phi * PHI);

    // Ensure normalization to guard against floating point inaccuracies, though it should be close to 1.0
    point.normalize(); 
    return point;
}

std::vector<SuperFibonacci::Vec4> SuperFibonacci::generate_points(size_t N) {
    std::vector<SuperFibonacci::Vec4> points;
    points.reserve(N);
    for (size_t i = 0; i < N; ++i) {
        points.push_back(point_on_s3(i, N));
    }
    return points;
}

} // namespace hartonomous::geometry
