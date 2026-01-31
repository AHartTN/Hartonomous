/**
 * @file ngram_extractor.hpp
 * @brief N-gram extraction and co-occurrence discovery
 *
 * Extracts n-grams from text and builds co-occurrence matrix for relation discovery.
 * Supports:
 * - Variable n-gram sizes (1 to max_n)
 * - Frequency counting
 * - Co-occurrence tracking within window
 * - Directional relations (A before B vs B before A)
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <string>
#include <vector>
#include <unordered_map>
#include <map>
#include <cstdint>
#include <cmath>

namespace Hartonomous {

/**
 * @brief N-gram with frequency and position data
 */
struct NGram {
    std::u32string text;              // The n-gram text (UTF-32)
    BLAKE3Pipeline::Hash hash;        // Deterministic hash
    uint32_t n;                       // N-gram size (1=unigram, 2=bigram, etc.)
    uint32_t frequency = 0;           // How many times it appears
    std::vector<uint32_t> positions;  // Starting positions in source text
};

/**
 * @brief Co-occurrence record (A appears near B)
 */
struct CoOccurrence {
    BLAKE3Pipeline::Hash ngram_a;
    BLAKE3Pipeline::Hash ngram_b;
    uint32_t count = 0;               // How many times A and B co-occur
    int32_t direction_sum = 0;        // Positive = A typically before B, negative = after
    double avg_distance = 0.0;        // Average distance between occurrences

    // Compute signal strength (0 to 1) based on count and distance
    double signal_strength() const {
        if (count == 0) return 0.0;
        // Closer = stronger, more frequent = stronger
        // Distance of 1 (adjacent) = 1.0, distance of 10 = ~0.1
        double proximity = 1.0 / (1.0 + avg_distance);
        // Log scale for frequency
        double freq_factor = std::log2(1.0 + count) / 10.0;
        return std::min(1.0, proximity * (0.5 + freq_factor));
    }

    // Is A typically before B?
    bool is_forward() const { return direction_sum > 0; }
};

/**
 * @brief Configuration for n-gram extraction
 */
struct NGramConfig {
    uint32_t min_n = 1;              // Minimum n-gram size
    uint32_t max_n = 8;              // Maximum n-gram size
    uint32_t min_frequency = 2;      // Minimum frequency to be considered significant
    uint32_t cooccurrence_window = 5; // Window size for co-occurrence detection
    bool track_positions = true;      // Track positions (memory intensive)
    bool track_direction = true;      // Track A-before-B vs B-before-A
};

/**
 * @brief N-gram extractor with co-occurrence discovery
 */
class NGramExtractor {
public:
    explicit NGramExtractor(const NGramConfig& config = NGramConfig());

    /**
     * @brief Extract all n-grams from text
     * @param text UTF-32 encoded text
     */
    void extract(const std::u32string& text);

    /**
     * @brief Get all extracted n-grams
     */
    const std::unordered_map<std::string, NGram>& ngrams() const { return ngrams_; }

    /**
     * @brief Get n-grams above frequency threshold
     */
    std::vector<const NGram*> significant_ngrams() const;

    /**
     * @brief Get all co-occurrences
     */
    const std::map<std::pair<std::string, std::string>, CoOccurrence>& cooccurrences() const {
        return cooccurrences_;
    }

    /**
     * @brief Get significant co-occurrences (above threshold)
     */
    std::vector<const CoOccurrence*> significant_cooccurrences(uint32_t min_count = 2) const;

    /**
     * @brief Clear all extracted data
     */
    void clear();

    /**
     * @brief Get statistics
     */
    size_t total_ngrams() const { return ngrams_.size(); }
    size_t total_cooccurrences() const { return cooccurrences_.size(); }

private:
    NGramConfig config_;

    // N-grams indexed by hash hex string
    std::unordered_map<std::string, NGram> ngrams_;

    // Co-occurrences indexed by (hash_a, hash_b) pair
    // Always stored with hash_a < hash_b lexicographically for consistency
    std::map<std::pair<std::string, std::string>, CoOccurrence> cooccurrences_;

    // Helper to compute n-gram hash
    BLAKE3Pipeline::Hash compute_hash(const std::u32string& text);

    // Helper to get hash hex string
    std::string hash_to_hex(const BLAKE3Pipeline::Hash& hash);

    // Record co-occurrence between two n-grams
    void record_cooccurrence(
        const std::string& hash_a, const BLAKE3Pipeline::Hash& full_hash_a,
        const std::string& hash_b, const BLAKE3Pipeline::Hash& full_hash_b,
        uint32_t pos_a, uint32_t pos_b);
};

}
