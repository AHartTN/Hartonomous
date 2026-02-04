/**
 * @file ngram_extractor.hpp
 * @brief N-gram extraction and co-occurrence discovery with advanced metrics
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
 * @brief N-gram with frequency, position, and statistical metrics
 */
struct NGram {
    std::u32string text;
    BLAKE3Pipeline::Hash hash;
    uint32_t n;
    uint32_t frequency = 0;
    std::vector<uint32_t> positions;
    
    // Advanced Metrics for Promotion
    double pmi = 0.0;
    double npmi = 0.0;
    double left_entropy = 0.0;
    double right_entropy = 0.0;
    uint32_t branching_factor = 0;    // Number of distinct continuations
    
    bool is_rle = false;              // Repeating atom sequence
};

/**
 * @brief Co-occurrence record (A appears near B)
 */
struct CoOccurrence {
    BLAKE3Pipeline::Hash ngram_a;
    BLAKE3Pipeline::Hash ngram_b;
    uint32_t count = 0;
    int32_t direction_sum = 0;
    double avg_distance = 0.0;

    double signal_strength() const {
        if (count == 0) return 0.0;
        double proximity = 1.0 / (1.0 + avg_distance);
        double freq_factor = std::log2(1.0 + count) / 10.0;
        return std::min(1.0, proximity * (0.5 + freq_factor));
    }

    bool is_forward() const { return direction_sum > 0; }
};

/**
 * @brief Configuration for n-gram extraction and promotion
 */
struct NGramConfig {
    uint32_t min_n = 1;
    uint32_t max_n = 8;
    uint32_t min_frequency = 2;
    uint32_t cooccurrence_window = 5;
    bool track_positions = true;
    bool track_direction = true;
    
    // Promotion thresholds
    double min_pmi = 1.0;
    double min_npmi = 0.1;
    double min_entropy = 0.5;
    uint32_t max_branching_factor = 50; // Filter out unstable prefixes
};

/**
 * @brief N-gram extractor: The "Semantic Microscope"
 */
class NGramExtractor {
public:
    explicit NGramExtractor(const NGramConfig& config = NGramConfig());

    void extract(const std::u32string& text);

    const std::unordered_map<BLAKE3Pipeline::Hash, NGram, HashHasher>& ngrams() const { return ngrams_; }
    std::vector<const NGram*> significant_ngrams() const;

    const std::map<std::pair<BLAKE3Pipeline::Hash, BLAKE3Pipeline::Hash>, CoOccurrence>& cooccurrences() const {
        return cooccurrences_;
    }

    std::vector<const CoOccurrence*> significant_cooccurrences(uint32_t min_count = 2) const;

    void clear();

    size_t total_ngrams() const { return ngrams_.size(); }
    size_t total_cooccurrences() const { return cooccurrences_.size(); }

private:
    NGramConfig config_;
    std::unordered_map<BLAKE3Pipeline::Hash, NGram, HashHasher> ngrams_;
    std::map<std::pair<BLAKE3Pipeline::Hash, BLAKE3Pipeline::Hash>, CoOccurrence> cooccurrences_;
    
    std::unordered_map<BLAKE3Pipeline::Hash, std::unordered_map<char32_t, uint32_t>, HashHasher> left_context_;
    std::unordered_map<BLAKE3Pipeline::Hash, std::unordered_map<char32_t, uint32_t>, HashHasher> right_context_;

    uint64_t total_unigrams_ = 0;

    void record_cooccurrence(const BLAKE3Pipeline::Hash& h1, const BLAKE3Pipeline::Hash& h2, uint32_t p1, uint32_t p2);
    void finalize_metrics();
    double calculate_entropy(const std::unordered_map<char32_t, uint32_t>& counts, uint32_t total);
};

}