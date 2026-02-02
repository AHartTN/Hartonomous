#pragma once

#include "../geometry/hopf_fibration.hpp"
#include "../geometry/super_fibonacci.hpp"
#include "../spatial/hilbert_curve_4d.hpp"
#include <blake3.h>
#include <Eigen/Core>
#include <cstdint>
#include <array>
#include <string>
#include <optional>
#include <vector>
#include <thread>
#include <future>
#include <numeric>

namespace hartonomous::unicode {

/**
 * @brief Complete Unicode Codepoint → 4D Hypersphere → Hilbert Curve Pipeline
 *
 * This class implements the complete geometric projection pipeline:
 *
 * 1. Unicode Codepoint (U+0000 to U+10FFFF)
 *       ↓ BLAKE3 hash for content-based positioning
 * 2. 128-bit hash value
 *       ↓ Super Fibonacci distribution
 * 3. Point on S³ (4D hypersphere)
 *       ↓ Embedded in 4D hypercube [-1,1]⁴
 * 4. 4D Coordinates
 *       ↓ Hilbert curve encoding
 * 5. 64-bit Hilbert Index (spatial key)
 *
 * Key Features:
 *   - Content-based: Same codepoint + context → same position
 *   - Deterministic: Reproducible across systems
 *   - Spatially coherent: Related content → nearby positions
 *   - Efficient indexing: Hilbert curve preserves locality
 *   - One-way: Coordinates → Hilbert value (never reversed)
 *
 * Use Cases:
 *   - Geometric hashing for deduplication
 *   - Spatial indexing for nearest-neighbor search
 *   - Content-addressable storage with geometric properties
 *   - Visualization of Unicode space in 4D/3D
 */
class CodepointProjection {
public:
    using Vec3 = Eigen::Vector3d;
    using Vec4 = Eigen::Vector4d;
    using HopfFibration = geometry::HopfFibration;
    using SuperFibonacci = geometry::SuperFibonacci;
    using HilbertCurve = spatial::HilbertCurve4D;

    /**
     * @brief Projection result containing all intermediate representations
     */
    struct ProjectionResult {
        uint32_t codepoint;              ///< Original Unicode codepoint
        std::array<uint8_t, 16> hash;    ///< BLAKE3 hash (128 bits)
        Vec4 s3_position;                ///< Position on 3-sphere (S³)
        Vec3 s2_projection;              ///< Hopf projection to 2-sphere (S²)
        Vec4 hypercube_coords;           ///< Coordinates in 4D hypercube [0,1]⁴
        HilbertCurve::HilbertIndex hilbert_index;          ///< Hilbert curve index (spatial key)

        /**
         * @brief Get a short identifier string (full 16 bytes of hash, hex)
         */
        std::string short_id() const {
            char buffer[33];
            for (int i = 0; i < 16; ++i) {
                snprintf(buffer + i * 2, 3, "%02x", hash[i]);
            }
            return std::string(buffer, 32);
        }
    };

    /**
     * @brief Project a Unicode codepoint to its geometric representation
     *
     * @param codepoint Unicode codepoint (U+0000 to U+10FFFF)
     * @param context Optional context string for content-based hashing
     * @param hilbert_bits Discretization bits for Hilbert curve (default 16)
     * @return ProjectionResult Complete projection data
     *
     * @throws std::invalid_argument if codepoint is invalid
     */
    static ProjectionResult project(
        uint32_t codepoint,
        const std::string& context = ""
    ) {
        // Validate Unicode codepoint
        if (codepoint > 0x10FFFF) {
            throw std::invalid_argument("Invalid Unicode codepoint (max U+10FFFF)");
        }

        ProjectionResult result;
        result.codepoint = codepoint;

        // Step 1: Hash codepoint + context using BLAKE3
        result.hash = hash_codepoint(codepoint, context);

        // Step 2: Map hash to point on S³ using Super Fibonacci
        result.s3_position = SuperFibonacci::hash_to_point(result.hash.data());

        // Step 3: Project to S² via Hopf fibration (for visualization)
        result.s2_projection = HopfFibration::forward(result.s3_position);

        // Step 4: Convert S³ point to hypercube coordinates [0,1]⁴
        result.hypercube_coords = s3_to_hypercube(result.s3_position);

        // Step 5: Encode as Hilbert curve index
        result.hilbert_index = HilbertCurve::encode(result.hypercube_coords);

        return result;
    }

    /**
     * @brief Project multiple codepoints in parallel (batch processing)
     *
     * This high-performance implementation partitions the work across all
     * available hardware threads to maximize throughput.
     *
     * @param codepoints Vector of Unicode codepoints
     * @param context Shared context string (optional)
     * @return std::vector<ProjectionResult> Projection results
     */
    static std::vector<ProjectionResult> project_batch(
        const std::vector<uint32_t>& codepoints,
        const std::string& context = ""
    ) {
        if (codepoints.empty()) {
            return {};
        }

        // Determine the optimal number of threads
        unsigned int num_threads = std::thread::hardware_concurrency();
        if (num_threads == 0) {
            num_threads = 1;
        }
        
        // If the job is small, don't bother with threading overhead
        if (codepoints.size() < num_threads * 4) {
             num_threads = 1;
        }

        std::vector<ProjectionResult> results;
        results.reserve(codepoints.size());

        std::vector<std::future<std::vector<ProjectionResult>>> futures;
        
        // Split the work into chunks for each thread
        size_t chunk_size = (codepoints.size() + num_threads - 1) / num_threads;
        for (size_t i = 0; i < codepoints.size(); i += chunk_size) {
            auto chunk_begin = codepoints.begin() + i;
            auto chunk_end = codepoints.begin() + std::min(i + chunk_size, codepoints.size());
            
            // Each chunk is processed asynchronously
            futures.push_back(std::async(std::launch::async, [=] {
                std::vector<ProjectionResult> partial_results;
                partial_results.reserve(chunk_end - chunk_begin);
                for (auto it = chunk_begin; it != chunk_end; ++it) {
                    partial_results.push_back(project(*it, context));
                }
                return partial_results;
            }));
        }

        // Collect the results from all futures
        for (auto& f : futures) {
            auto partial_results = f.get();
            results.insert(results.end(), partial_results.begin(), partial_results.end());
        }

        return results;
    }


    /**
     * @brief Project a UTF-8 string to a sequence of geometric points
     *
     * @param utf8_string UTF-8 encoded string
     * @param hilbert_bits Hilbert discretization bits
     * @return std::vector<ProjectionResult> One result per codepoint
     */
    static std::vector<ProjectionResult> project_string(
        const std::string& utf8_string
    ) {
        auto codepoints = utf8_to_codepoints(utf8_string);
        return project_batch(codepoints, utf8_string);
    }

    /**
     * @brief Compute geometric distance between two projected codepoints
     *
     * Uses geodesic distance on S³ for accurate measurement.
     *
     * @param p1 First projection result
     * @param p2 Second projection result
     * @return double Distance (angle in radians, range [0, π])
     */
    static double geometric_distance(const ProjectionResult& p1, const ProjectionResult& p2) {
        return HopfFibration::distance_s3(p1.s3_position, p2.s3_position);
    }

    /**
     * @brief Compute Hilbert curve distance (1D approximation)
     *
     * Fast approximate distance using curve indices. The returned distance is a
     * 128-bit value.
     *
     * @param p1 First projection result
     * @param p2 Second projection result
     * @return HilbertCurve::HilbertIndex Curve distance
     */
    static HilbertCurve::HilbertIndex hilbert_distance(const ProjectionResult& p1, const ProjectionResult& p2) {
        return HilbertCurve::curve_distance(p1.hilbert_index, p2.hilbert_index);
    }

private:
    /**
     * @brief Hash a Unicode codepoint with optional context using BLAKE3
     *
     * @param codepoint Unicode codepoint
     * @param context Optional context string
     * @return std::array<uint8_t, 16> BLAKE3 hash (128 bits)
     */
    static std::array<uint8_t, 16> hash_codepoint(uint32_t codepoint, const std::string& context) {
        blake3_hasher hasher;
        blake3_hasher_init(&hasher);

        // Hash the codepoint (4 bytes, little-endian)
        uint8_t cp_bytes[4] = {
            static_cast<uint8_t>(codepoint & 0xFF),
            static_cast<uint8_t>((codepoint >> 8) & 0xFF),
            static_cast<uint8_t>((codepoint >> 16) & 0xFF),
            static_cast<uint8_t>((codepoint >> 24) & 0xFF)
        };
        blake3_hasher_update(&hasher, cp_bytes, 4);

        // Hash the context if provided
        if (!context.empty()) {
            blake3_hasher_update(&hasher, context.data(), context.size());
        }

        // Finalize and extract 128-bit hash
        std::array<uint8_t, 16> hash;
        blake3_hasher_finalize(&hasher, hash.data(), 16);

        return hash;
    }

    /**
     * @brief Convert S³ point to 4D hypercube coordinates
     *
     * Maps from unit sphere (|p| = 1, p ∈ [-1,1]⁴) to unit hypercube [0,1]⁴
     *
     * @param s3_point Point on S³
     * @return Vec4 Coordinates in [0,1]⁴
     */
    static Vec4 s3_to_hypercube(const Vec4& s3_point) {
        Vec4 hypercube;
        for (int i = 0; i < 4; ++i) {
            // Map [-1, 1] → [0, 1]
            hypercube[i] = (s3_point[i] + 1.0) / 2.0;
        }
        return hypercube;
    }

    /**
     * @brief Decode UTF-8 string to Unicode codepoints
     *
     * @param utf8 UTF-8 encoded string
     * @return std::vector<uint32_t> Vector of Unicode codepoints
     */
    static std::vector<uint32_t> utf8_to_codepoints(const std::string& utf8) {
        std::vector<uint32_t> codepoints;
        codepoints.reserve(utf8.size()); // Upper bound

        size_t i = 0;
        while (i < utf8.size()) {
            uint32_t codepoint;
            size_t bytes_consumed = decode_utf8_char(utf8.data() + i, utf8.size() - i, codepoint);

            if (bytes_consumed == 0) {
                // Invalid UTF-8 sequence, skip byte
                i++;
                continue;
            }

            codepoints.push_back(codepoint);
            i += bytes_consumed;
        }

        return codepoints;
    }

    /**
     * @brief Decode a single UTF-8 character
     *
     * @param data Pointer to UTF-8 data
     * @param len Available bytes
     * @param[out] codepoint Decoded codepoint
     * @return size_t Number of bytes consumed (0 if invalid)
     */
    static size_t decode_utf8_char(const char* data, size_t len, uint32_t& codepoint) {
        if (len == 0) return 0;

        uint8_t b0 = static_cast<uint8_t>(data[0]);

        // 1-byte sequence (0xxxxxxx)
        if ((b0 & 0x80) == 0) {
            codepoint = b0;
            return 1;
        }

        // 2-byte sequence (110xxxxx 10xxxxxx)
        if ((b0 & 0xE0) == 0xC0 && len >= 2) {
            uint8_t b1 = static_cast<uint8_t>(data[1]);
            if ((b1 & 0xC0) == 0x80) {
                codepoint = ((b0 & 0x1F) << 6) | (b1 & 0x3F);
                return 2;
            }
        }

        // 3-byte sequence (1110xxxx 10xxxxxx 10xxxxxx)
        if ((b0 & 0xF0) == 0xE0 && len >= 3) {
            uint8_t b1 = static_cast<uint8_t>(data[1]);
            uint8_t b2 = static_cast<uint8_t>(data[2]);
            if ((b1 & 0xC0) == 0x80 && (b2 & 0xC0) == 0x80) {
                codepoint = ((b0 & 0x0F) << 12) | ((b1 & 0x3F) << 6) | (b2 & 0x3F);
                return 3;
            }
        }

        // 4-byte sequence (11110xxx 10xxxxxx 10xxxxxx 10xxxxxx)
        if ((b0 & 0xF8) == 0xF0 && len >= 4) {
            uint8_t b1 = static_cast<uint8_t>(data[1]);
            uint8_t b2 = static_cast<uint8_t>(data[2]);
            uint8_t b3 = static_cast<uint8_t>(data[3]);
            if ((b1 & 0xC0) == 0x80 && (b2 & 0xC0) == 0x80 && (b3 & 0xC0) == 0x80) {
                codepoint = ((b0 & 0x07) << 18) | ((b1 & 0x3F) << 12) |
                           ((b2 & 0x3F) << 6) | (b3 & 0x3F);
                return 4;
            }
        }

        return 0; // Invalid sequence
    }
};

} // namespace hartonomous::unicode
