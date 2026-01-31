#include <unicode/ingestor/node_generator.hpp>
#include <cmath>
#include <algorithm>

namespace Hartonomous::unicode {

double NodeGenerator::halton(size_t index, int base) {
    double result = 0;
    double f = 1.0 / base;
    size_t i = index;
    while (i > 0) {
        result += f * (i % base);
        i /= base;
        f /= base;
    }
    return result;
}

NodeGenerator::Vec4 NodeGenerator::generate_node(size_t i) {
    // 1. Generate 3D low-discrepancy point in [0,1)^3 using Halton sequence (bases 2, 3, 5)
    // We offset i by 1 because Halton(0) is often (0,0,0) which can be degenerate.
    double u = halton(i + 1, 2);
    double v = halton(i + 1, 3);
    double w = halton(i + 1, 5);

    // 2. Map to S3 via Hopf coordinates
    double alpha = 2.0 * M_PI * u;
    double beta = 2.0 * M_PI * v;
    
    // Uniform theta on [0, PI] for S3 uniform sampling
    // theta = arccos(1 - 2*w)
    double theta = std::acos(std::clamp(1.0 - 2.0 * w, -1.0, 1.0));

    // Hopf parameterization:
    // x0 = cos(alpha) * sin(theta)
    // x1 = sin(alpha) * sin(theta)
    // x2 = cos(beta) * cos(theta)
    // x3 = sin(beta) * cos(theta)
    
    double sin_theta = std::sin(theta);
    double cos_theta = std::cos(theta);

    Vec4 p;
    p[0] = std::cos(alpha) * sin_theta;
    p[1] = std::sin(alpha) * sin_theta;
    p[2] = std::cos(beta) * cos_theta;
    p[3] = std::sin(beta) * cos_theta;

    // Normalize for safety
    return p.normalized();
}

} // namespace Hartonomous::unicode
