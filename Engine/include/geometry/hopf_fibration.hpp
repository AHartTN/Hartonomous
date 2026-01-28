#pragma once

#include <Eigen/Core>
#include <complex>
#include <cmath>

namespace hartonomous::geometry {

/**
 * @brief Hopf Fibration: S³ → S² mapping
 *
 * The Hopf fibration is a continuous mapping from the 3-sphere (S³) in 4D space
 * to the 2-sphere (S²) in 3D space. Each point on S² has a circle (S¹) of
 * preimages on S³, forming a beautiful fibration structure.
 *
 * Mathematical Definition:
 *   For a point (z₁, z₂) ∈ ℂ² on S³ (where |z₁|² + |z₂|² = 1):
 *
 *   h(z₁, z₂) = (|z₁|² - |z₂|², 2Re(z₁z̄₂), 2Im(z₁z̄₂)) ∈ ℝ³
 *
 * This maps S³ → S² with each fiber being a circle (Hopf link).
 *
 * Use Cases:
 *   - Uniformly distributing high-dimensional data on spheres
 *   - Generating aesthetically pleasing 3D projections from 4D data
 *   - Content-based geometric hashing for Unicode codepoints
 */
class HopfFibration {
public:
    using Vec3 = Eigen::Vector3d;
    using Vec4 = Eigen::Vector4d;
    using Complex = std::complex<double>;

    /**
     * @brief Forward Hopf map: S³ → S²
     *
     * Projects a point on the 3-sphere (4D) to a point on the 2-sphere (3D).
     *
     * @param s3_point Point on S³ in ℝ⁴ (must be normalized: ||p|| = 1)
     * @return Vec3 Point on S² in ℝ³ (automatically normalized)
     *
     * @note Input is assumed to be on S³. For unnormalized inputs, normalize first.
     */
    static Vec3 forward(const Vec4& s3_point) {
        // Interpret the 4D point as two complex numbers (z₁, z₂)
        Complex z1(s3_point[0], s3_point[1]);
        Complex z2(s3_point[2], s3_point[3]);

        // Hopf map formula:
        // x = |z₁|² - |z₂|²
        // y = 2 * Re(z₁ * conj(z₂))
        // z = 2 * Im(z₁ * conj(z₂))

        double x = std::norm(z1) - std::norm(z2);

        Complex z1_conj_z2 = z1 * std::conj(z2);
        double y = 2.0 * z1_conj_z2.real();
        double z = 2.0 * z1_conj_z2.imag();

        return Vec3(x, y, z);
    }

    /**
     * @brief Inverse Hopf map: S² → S³ (one fiber point)
     *
     * Lifts a point on S² back to S³. Note that this is a fiber bundle, so
     * infinitely many points on S³ map to the same point on S². This function
     * returns one canonical point on the fiber.
     *
     * @param s2_point Point on S² in ℝ³ (must be normalized)
     * @param fiber_angle Phase angle (0 to 2π) to select a point on the fiber circle
     * @return Vec4 Point on S³ in ℝ⁴
     *
     * @note The fiber_angle allows you to traverse the entire circle of preimages.
     */
    static Vec4 inverse(const Vec3& s2_point, double fiber_angle = 0.0) {
        double x = s2_point[0];
        double y = s2_point[1];
        double z = s2_point[2];

        // Stereographic projection from S² to ℂ
        // Then lift to S³ using the Hopf inverse

        // One standard construction:
        // |z₁|² = (1 + x) / 2
        // |z₂|² = (1 - x) / 2
        // arg(z₁/z₂) relates to y, z

        double r1_sq = (1.0 + x) / 2.0;
        double r2_sq = (1.0 - x) / 2.0;

        // Avoid sqrt of negative (numerical safety)
        r1_sq = std::max(0.0, r1_sq);
        r2_sq = std::max(0.0, r2_sq);

        double r1 = std::sqrt(r1_sq);
        double r2 = std::sqrt(r2_sq);

        // Phase calculation
        double phase = std::atan2(z, y);

        // Build z₁ and z₂ with fiber parameter
        Complex z1 = r1 * std::polar(1.0, fiber_angle);
        Complex z2 = r2 * std::polar(1.0, fiber_angle - phase);

        return Vec4(z1.real(), z1.imag(), z2.real(), z2.imag());
    }

    /**
     * @brief Normalize a 4D vector to lie on S³
     *
     * @param v Input vector in ℝ⁴
     * @return Vec4 Normalized vector on S³
     */
    static Vec4 normalize_s3(const Vec4& v) {
        double norm = v.norm();
        if (norm < 1e-15) {
            // Degenerate case: return a default point
            return Vec4(1.0, 0.0, 0.0, 0.0);
        }
        return v / norm;
    }

    /**
     * @brief Normalize a 3D vector to lie on S²
     *
     * @param v Input vector in ℝ³
     * @return Vec3 Normalized vector on S²
     */
    static Vec3 normalize_s2(const Vec3& v) {
        double norm = v.norm();
        if (norm < 1e-15) {
            // Degenerate case: return north pole
            return Vec3(0.0, 0.0, 1.0);
        }
        return v / norm;
    }

    /**
     * @brief Geodesic distance on S³ between two points
     *
     * @param p1 First point on S³
     * @param p2 Second point on S³
     * @return double Distance (angle in radians, range [0, π])
     */
    static double distance_s3(const Vec4& p1, const Vec4& p2) {
        double dot = p1.dot(p2);
        dot = std::clamp(dot, -1.0, 1.0); // Numerical safety
        return std::acos(dot);
    }

    /**
     * @brief Check if a point lies on S³ (within tolerance)
     *
     * @param p Point to check
     * @param tolerance Allowed deviation from unit norm
     * @return true if ||p|| ≈ 1
     */
    static bool is_on_s3(const Vec4& p, double tolerance = 1e-10) {
        double norm_sq = p.squaredNorm();
        return std::abs(norm_sq - 1.0) < tolerance;
    }
};

} // namespace hartonomous::geometry
