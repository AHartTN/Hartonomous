/**
 * @file blake3_pipeline.cpp
 * @brief BLAKE3 hashing implementation
 */

#include <hashing/blake3_pipeline.hpp>
#include <cstring>
#include <sstream>
#include <iomanip>
#include <thread>
#include <algorithm>

namespace Hartonomous {

BLAKE3Pipeline::Hash BLAKE3Pipeline::hash(const void* data, size_t len) {
    Hash result;

    // BLAKE3 one-shot hashing (automatically uses best SIMD)
    blake3_hasher hasher;
    blake3_hasher_init(&hasher);
    blake3_hasher_update(&hasher, data, len);
    blake3_hasher_finalize(&hasher, result.data(), BLAKE3_OUT_LEN);

    return result;
}

BLAKE3Pipeline::Hash BLAKE3Pipeline::hash_codepoint(char32_t codepoint) {
    // Hash the codepoint as 4 bytes (little-endian)
    uint8_t bytes[4];
    bytes[0] = (codepoint >> 0) & 0xFF;
    bytes[1] = (codepoint >> 8) & 0xFF;
    bytes[2] = (codepoint >> 16) & 0xFF;
    bytes[3] = (codepoint >> 24) & 0xFF;

    return hash(bytes, 4);
}

std::vector<BLAKE3Pipeline::Hash> BLAKE3Pipeline::hash_batch(const std::vector<std::string>& inputs) {
    std::vector<Hash> results(inputs.size());

    // Simple parallel processing
    const size_t num_threads = std::min(
        (size_t)std::thread::hardware_concurrency(),
        inputs.size()
    );

    if (num_threads <= 1 || inputs.size() < 100) {
        // Serial for small batches
        for (size_t i = 0; i < inputs.size(); ++i) {
            results[i] = hash(inputs[i]);
        }
    } else {
        // Parallel for large batches
        std::vector<std::thread> threads;
        size_t chunk_size = (inputs.size() + num_threads - 1) / num_threads;

        for (size_t t = 0; t < num_threads; ++t) {
            size_t start = t * chunk_size;
            size_t end = std::min(start + chunk_size, inputs.size());

            if (start >= inputs.size()) break;

            threads.emplace_back([&, start, end]() {
                for (size_t i = start; i < end; ++i) {
                    results[i] = hash(inputs[i]);
                }
            });
        }

        for (auto& thread : threads) {
            thread.join();
        }
    }

    return results;
}

std::string BLAKE3Pipeline::to_hex(const Hash& hash) {
    std::ostringstream oss;
    oss << std::hex << std::setfill('0');

    for (uint8_t byte : hash) {
        oss << std::setw(2) << (int)byte;
    }

    return oss.str();
}

BLAKE3Pipeline::Hash BLAKE3Pipeline::from_hex(const std::string& hex) {
    Hash result;

    if (hex.size() != BLAKE3_OUT_LEN * 2) {
        throw std::invalid_argument("Invalid hex string length for BLAKE3 hash");
    }

    for (size_t i = 0; i < BLAKE3_OUT_LEN; ++i) {
        std::string byte_str = hex.substr(i * 2, 2);
        result[i] = (uint8_t)std::stoul(byte_str, nullptr, 16);
    }

    return result;
}

} // namespace Hartonomous
