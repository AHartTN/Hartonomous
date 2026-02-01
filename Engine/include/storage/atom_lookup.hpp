#pragma once

#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <Eigen/Core>
#include <unordered_map>
#include <optional>
#include <string>
#include <cstdint>

namespace Hartonomous {

/**
 * @brief Lookup service for pre-seeded Unicode atoms
 *
 * The full Unicode codespace is seeded during database initialization
 * with semantically-ordered S³ positions (via UCA, script grouping, etc.).
 * This class provides efficient lookup of those pre-computed atoms.
 *
 * IMPORTANT: Text/content ingestion MUST use this lookup to get the
 * correct semantic positions. Do NOT compute positions from hash -
 * that would bypass the semantic ordering.
 */
class AtomLookup {
public:
    using Vec4 = Eigen::Vector4d;
    using Hash = BLAKE3Pipeline::Hash;
    using HilbertIndex = hartonomous::spatial::HilbertCurve4D::HilbertIndex;

    struct AtomInfo {
        Hash id;                  // BLAKE3(codepoint)
        Hash physicality_id;      // BLAKE3(position)
        Vec4 position;            // S³ position (semantically ordered)
        HilbertIndex hilbert_index;  // Hilbert curve index
        uint32_t codepoint;
    };

    explicit AtomLookup(PostgresConnection& db);

    /**
     * @brief Look up atom by codepoint
     *
     * Returns the pre-seeded atom with its semantically-ordered position.
     * Returns nullopt if codepoint wasn't seeded (shouldn't happen for valid Unicode).
     */
    std::optional<AtomInfo> lookup(uint32_t codepoint);

    /**
     * @brief Look up multiple atoms at once (batch query)
     *
     * More efficient than individual lookups for processing text.
     */
    std::unordered_map<uint32_t, AtomInfo> lookup_batch(const std::vector<uint32_t>& codepoints);

    /**
     * @brief Preload all atoms into memory for fast lookup
     *
     * For high-throughput ingestion, call this once to cache all 1.1M atoms.
     * Uses ~200MB of memory but makes lookups instant.
     */
    void preload_all();

    /**
     * @brief Check if atom cache is loaded
     */
    bool is_preloaded() const { return preloaded_; }

private:
    PostgresConnection& db_;
    std::unordered_map<uint32_t, AtomInfo> cache_;
    bool preloaded_ = false;

    static Hash uuid_to_hash(const std::string& uuid);
    static Vec4 parse_geometry(const std::string& geom_hex);
    static HilbertIndex parse_hilbert(const std::string& hilbert_str);
};

} // namespace Hartonomous
