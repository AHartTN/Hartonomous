/**
 * @file ngram_extractor.hpp
 * @brief Suffix array-based composition discovery from text
 *
 * Uses SA+LCP to find all repeated substrings (no length limit).
 * Compositions emerge from frequency — not from an arbitrary n-gram window.
 * Relations are computed externally from adjacency, not co-occurrence.
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <string>
#include <vector>
#include <unordered_map>
#include <map>
#include <cstdint>
#include <cmath>
#include <cstring>

namespace Hartonomous {

/**
 * @brief A discovered composition: repeated substring with statistics
 */
struct NGram {
    std::u32string text;
    BLAKE3Pipeline::Hash hash;
    uint32_t n;                       // Length in codepoints
    uint32_t frequency = 0;
    std::vector<uint32_t> positions;  // Sorted positions in text where this appears
    
    // Statistical metrics
    double pmi = 0.0;
    double npmi = 0.0;
    double left_entropy = 0.0;
    double right_entropy = 0.0;
    uint32_t branching_factor = 0;
    
    bool is_rle = false;              // Repeating atom sequence (e.g., "aaa")
    std::string pattern_signature;    // Structural pattern (e.g., "XYYX" for "abba")
};

/**
 * @brief Configuration for composition discovery
 */
struct NGramConfig {
    uint32_t min_n = 1;
    uint32_t max_n = 256;             // Practical cap, not semantic — SA handles any length
    uint32_t min_frequency = 3;       // Minimum occurrences to be a composition
    bool track_positions = true;
    bool track_direction = true;
    
    // Promotion thresholds for multi-codepoint compositions
    double min_pmi = 1.0;
    double min_npmi = 0.1;
    double min_entropy = 0.5;
    uint32_t max_branching_factor = 50;

    // Legacy fields (unused, kept for compat)
    uint32_t cooccurrence_window = 5;
};

/**
 * @brief Suffix array-based composition discoverer
 *
 * Discovers all repeated substrings in text via SA+LCP.
 * No arbitrary n-gram window. No co-occurrence computation.
 * Relations are computed by the caller from position/adjacency data.
 */
class NGramExtractor {
public:
    explicit NGramExtractor(const NGramConfig& config = NGramConfig());

    void extract(const std::u32string& text);

    const std::unordered_map<BLAKE3Pipeline::Hash, NGram, HashHasher>& ngrams() const { return ngrams_; }
    std::vector<const NGram*> significant_ngrams() const;

    void clear();

    size_t total_ngrams() const { return ngrams_.size(); }
    uint64_t total_unigrams() const { return total_unigrams_; }

private:
    NGramConfig config_;
    std::unordered_map<BLAKE3Pipeline::Hash, NGram, HashHasher> ngrams_;
    
    std::unordered_map<BLAKE3Pipeline::Hash, std::unordered_map<char32_t, uint32_t>, HashHasher> left_context_;
    std::unordered_map<BLAKE3Pipeline::Hash, std::unordered_map<char32_t, uint32_t>, HashHasher> right_context_;

    uint64_t total_unigrams_ = 0;

    void finalize_metrics();
    double calculate_entropy(const std::unordered_map<char32_t, uint32_t>& counts, uint32_t total);
    static std::string compute_pattern_signature(const std::u32string& text);
};

}