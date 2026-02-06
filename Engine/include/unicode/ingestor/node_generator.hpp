#pragma once

#include <Eigen/Core>

namespace Hartonomous::unicode {

/**
 * @brief Deterministic node generator for S³ using Super Fibonacci lattice.
 * 
 * Uses Fibonacci spiral on S² lifted to S³ via Hopf fibration.
 * Golden ratio (φ) drives S² longitude, Plastic constant (ψ) drives fiber phase.
 * Consecutive indices → nearby S³ points (locality-preserving).
 */
class NodeGenerator {
public:
    using Vec4 = Eigen::Vector4d;

    /**
     * @brief Generate the i-th node on S³ out of N total nodes
     * @param i Index of the node (0-based)
     * @param N Total number of nodes being distributed
     * @return Vec4 Point on unit S³
     */
    static Vec4 generate_node(size_t i, size_t N);

    /// Total Unicode codespace (all possible codepoints)
    static constexpr size_t UNICODE_TOTAL = 0x110000; // 1,114,112
};

} // namespace Hartonomous::unicode
