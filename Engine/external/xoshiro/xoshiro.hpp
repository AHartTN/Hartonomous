#pragma once

#include <cstdint>
#include <array>
#include <limits>

namespace hartonomous::prng {

/**
 * @brief A C++ wrapper for the xoshiro256++ 1.0 PRNG.
 *
 * This is a high-performance generator with excellent statistical properties.
 * It is adapted from the public domain C implementation by David Blackman and
 * Sebastiano Vigna.
 *
 * This class is designed to be a drop-in replacement for standard library
 * generators like std::mt19937_64 and satisfies the
 * C++ UniformRandomBitGenerator concept.
 *
 * @see http://prng.di.unimi.it/xoshiro256pp.c
 */
class xoshiro256pp {
public:
    using result_type = uint64_t;

    /**
     * @brief Seeds the generator from two 64-bit integers (e.g., a 128-bit hash).
     *
     * The 256-bit state is initialized by taking the two 64-bit seed values
     * and creating two more by XORing with fixed constants to ensure a
     * well-distributed initial state. This is much faster than using a
     * secondary PRNG like splitmix64 for seeding.
     *
     * @param seed_hi The high 64 bits of the 128-bit seed.
     * @param seed_lo The low 64 bits of the 128-bit seed.
     */
    explicit xoshiro256pp(uint64_t seed_hi, uint64_t seed_lo) {
        state_[0] = seed_hi;
        state_[1] = seed_lo;
        // Just needs to not be all zeros. This is a simple, fast way to expand the seed.
        state_[2] = seed_hi ^ 0x9E3779B97F4A7C15ULL;
        state_[3] = seed_lo ^ 0xBF58476D1CE4E5B9ULL;
    }

    /**
     * @brief Returns the minimum possible value (0).
     */
    static constexpr result_type min() {
        return std::numeric_limits<result_type>::min();
    }

    /**
     * @brief Returns the maximum possible value.
     */
    static constexpr result_type max() {
        return std::numeric_limits<result_type>::max();
    }

    /**
     * @brief Generates the next pseudo-random 64-bit integer.
     */
    result_type operator()() {
        const result_type result = rotl(state_[0] + state_[3], 23) + state_[0];
        const result_type t = state_[1] << 17;

        state_[2] ^= state_[0];
        state_[3] ^= state_[1];
        state_[1] ^= state_[2];
        state_[0] ^= state_[3];

        state_[2] ^= t;
        state_[3] = rotl(state_[3], 45);

        return result;
    }

private:
    std::array<uint64_t, 4> state_;

    static inline result_type rotl(const result_type x, int k) {
        return (x << k) | (x >> (64 - k));
    }
};

} // namespace hartonomous::prng
