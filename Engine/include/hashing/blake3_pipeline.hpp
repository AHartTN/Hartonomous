/**
 * @file blake3_pipeline.hpp
 * @brief SIMD-optimized BLAKE3 hashing pipeline
 */

#pragma once

#include <vector>
#include <array>
#include <cstdint>
#include <string>
#include <string_view>

extern "C" {
#include <blake3.h>
}

namespace Hartonomous {

/**
 * @brief BLAKE3 hashing with SIMD optimization
 *
 * Provides content-addressable hashing for universal deduplication.
 * SAME CONTENT = SAME HASH = STORED ONCE
 */
class BLAKE3Pipeline {
public:
    static constexpr size_t HASH_SIZE = 16; // 128 bits
    using Hash = std::array<uint8_t, HASH_SIZE>;

    /**
     * @brief Hash single buffer
     * @param data Input data
     * @param len Length in bytes
     * @return 16-byte BLAKE3 hash
     */
    static Hash hash(const void* data, size_t len);

    /**
     * @brief Hash string
     */
    static Hash hash(std::string_view str) {
        return hash(str.data(), str.size());
    }

    /**
     * @brief Hash vector
     */
    static Hash hash(const std::vector<uint8_t>& data) {
        return hash(data.data(), data.size());
    }

    /**
     * @brief Hash codepoint (for atoms)
     */
    static Hash hash_codepoint(char32_t codepoint);

    /**
     * @brief Batch hash multiple inputs (parallel)
     * @param inputs Vector of input buffers
     * @return Vector of hashes (same order)
     */
    static std::vector<Hash> hash_batch(const std::vector<std::string>& inputs);

    /**
     * @brief Convert hash to hex string
     */
    static std::string to_hex(const Hash& hash);

    /**
     * @brief Convert hex string to hash
     */
    static Hash from_hex(const std::string& hex);

    /**
     * @brief Compare two hashes
     */
    static bool equal(const Hash& a, const Hash& b) {
        return a == b;
    }
};

} // namespace Hartonomous
