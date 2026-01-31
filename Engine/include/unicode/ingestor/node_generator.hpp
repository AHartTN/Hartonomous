#pragma once

#include <Eigen/Core>
#include <vector>

namespace Hartonomous::unicode {

/**
 * @brief Deterministic node generator for S³ using low-discrepancy sequences.
 * 
 * Uses Halton sequence in 3D mapped to S³ via Hopf coordinates.
 */
class NodeGenerator {
public:
    using Vec4 = Eigen::Vector4d;

    /**
     * @brief Generate the i-th node on S³
     * @param i Index of the node
     * @return Vec4 Point on S³
     */
    static Vec4 generate_node(size_t i);

private:
    /**
     * @brief Van der Corput sequence (base b)
     */
    static double halton(size_t index, int base);
};

} // namespace Hartonomous::unicode
