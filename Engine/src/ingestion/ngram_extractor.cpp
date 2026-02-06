/**
 * @file ngram_extractor.cpp
 * @brief Suffix array-based composition discovery
 *
 * Uses libdivsufsort for O(N log N) suffix array construction, Kasai's algorithm
 * for O(N) LCP array, then scans SA+LCP to discover all repeated substrings.
 * No arbitrary n-gram window. No co-occurrence computation.
 * Positions are stored so the caller can derive relations from adjacency.
 */

#include <ingestion/ngram_extractor.hpp>
#include <algorithm>
#include <cmath>
#include <chrono>
#include <iostream>
#include <iomanip>
#include <divsufsort.h>

namespace Hartonomous {

using Clock = std::chrono::steady_clock;
static double ms_since(Clock::time_point t0) {
    return std::chrono::duration<double, std::milli>(Clock::now() - t0).count();
}

static BLAKE3Pipeline::Hash hash_codepoints(const char32_t* data, uint32_t len) {
    blake3_hasher hasher;
    blake3_hasher_init(&hasher);
    for (uint32_t i = 0; i < len; ++i) {
        char32_t cp = data[i];
        uint8_t bytes[4] = {
            static_cast<uint8_t>(cp & 0xFF),
            static_cast<uint8_t>((cp >> 8) & 0xFF),
            static_cast<uint8_t>((cp >> 16) & 0xFF),
            static_cast<uint8_t>((cp >> 24) & 0xFF)
        };
        blake3_hasher_update(&hasher, bytes, 4);
    }
    BLAKE3Pipeline::Hash hash;
    blake3_hasher_finalize(&hasher, hash.data(), BLAKE3Pipeline::HASH_SIZE);
    return hash;
}

std::string NGramExtractor::compute_pattern_signature(const std::u32string& text) {
    if (text.size() <= 1) return "";
    std::string sig;
    sig.reserve(text.size());
    std::unordered_map<char32_t, char> mapping;
    char next_sym = 'X';
    for (char32_t cp : text) {
        auto it = mapping.find(cp);
        if (it == mapping.end()) {
            if (next_sym > 'Z') next_sym = 'a'; // overflow to lowercase
            mapping[cp] = next_sym;
            sig += next_sym++;
        } else {
            sig += it->second;
        }
    }
    return sig;
}

NGramExtractor::NGramExtractor(const NGramConfig& config) : config_(config) {}

void NGramExtractor::extract(const std::u32string& text) {
    if (text.empty()) return;
    const uint32_t N = static_cast<uint32_t>(text.size());
    auto t_total = Clock::now();

    // === Phase 1: Serialize codepoints → big-endian bytes for divsufsort ===
    auto t0 = Clock::now();
    const uint32_t byte_len = N * 4;
    std::vector<uint8_t> bytes(byte_len);
    for (uint32_t i = 0; i < N; ++i) {
        char32_t cp = text[i];
        bytes[i * 4 + 0] = static_cast<uint8_t>((cp >> 24) & 0xFF);
        bytes[i * 4 + 1] = static_cast<uint8_t>((cp >> 16) & 0xFF);
        bytes[i * 4 + 2] = static_cast<uint8_t>((cp >> 8) & 0xFF);
        bytes[i * 4 + 3] = static_cast<uint8_t>(cp & 0xFF);
    }
    std::cout << "    [sa] serialize: " << std::fixed << std::setprecision(0)
              << ms_since(t0) << "ms (" << N << " codepoints)" << std::endl;

    // === Phase 2: Build suffix array ===
    t0 = Clock::now();
    std::vector<saidx_t> sa(byte_len);
    if (divsufsort(bytes.data(), sa.data(), static_cast<saidx_t>(byte_len)) != 0) {
        std::cerr << "    [sa] ERROR: divsufsort failed" << std::endl;
        return;
    }
    std::cout << "    [sa] suffix array: " << ms_since(t0) << "ms" << std::endl;

    // === Phase 3: Build codepoint-aligned SA and LCP ===
    t0 = Clock::now();

    // Extract codepoint-aligned entries from byte SA
    std::vector<uint32_t> cp_sa;
    cp_sa.reserve(N);
    for (int32_t i = 0; i < static_cast<int32_t>(byte_len); ++i) {
        if (sa[i] % 4 == 0 && sa[i] + 4 <= static_cast<saidx_t>(byte_len)) {
            cp_sa.push_back(static_cast<uint32_t>(sa[i] / 4));
        }
    }

    // Free byte-level SA
    { std::vector<uint8_t>().swap(bytes); }
    { std::vector<saidx_t>().swap(sa); }

    // Compute codepoint LCP between consecutive SA entries
    std::vector<uint32_t> cp_lcp(cp_sa.size(), 0);
    for (size_t i = 1; i < cp_sa.size(); ++i) {
        uint32_t pos_a = cp_sa[i - 1];
        uint32_t pos_b = cp_sa[i];
        uint32_t max_cmp = std::min(N - pos_a, N - pos_b);
        max_cmp = std::min(max_cmp, config_.max_n);
        uint32_t l = 0;
        while (l < max_cmp && text[pos_a + l] == text[pos_b + l]) ++l;
        cp_lcp[i] = l;
    }
    std::cout << "    [sa] codepoint SA+LCP: " << ms_since(t0) << "ms ("
              << cp_sa.size() << " entries)" << std::endl;

    // === Phase 4: Discover compositions — all repeated substrings ===
    // For each length n, scan through the SA. Groups of consecutive entries
    // with LCP >= n share the same n-length prefix = same composition.
    t0 = Clock::now();
    uint64_t total_discovered = 0;
    uint64_t total_promoted = 0;

    // Unigrams first — always included (every codepoint is an atom)
    for (size_t i = 0; i < cp_sa.size(); ) {
        uint32_t pos = cp_sa[i];
        char32_t cp = text[pos];
        
        // Count consecutive entries with same first codepoint
        size_t j = i + 1;
        while (j < cp_sa.size() && cp_lcp[j] >= 1 && text[cp_sa[j]] == cp) ++j;
        
        uint32_t freq = static_cast<uint32_t>(j - i);
        auto hash = hash_codepoints(&text[pos], 1);
        
        auto& ngram = ngrams_[hash];
        if (ngram.frequency == 0) {
            ngram.text = text.substr(pos, 1);
            ngram.hash = hash;
            ngram.n = 1;
        }
        ngram.frequency = freq;
        total_unigrams_ += freq;
        
        // Store positions for unigrams
        if (config_.track_positions) {
            ngram.positions.reserve(freq);
            for (size_t k = i; k < j; ++k) ngram.positions.push_back(cp_sa[k]);
            std::sort(ngram.positions.begin(), ngram.positions.end());
        }
        
        // Context for entropy
        uint32_t sample_step = std::max(1u, freq / 64);
        for (size_t k = i; k < j; k += sample_step) {
            uint32_t p = cp_sa[k];
            if (p > 0) left_context_[hash][text[p - 1]]++;
            if (p + 1 < N) right_context_[hash][text[p + 1]]++;
        }
        
        total_discovered++;
        i = j;
    }

    // Multi-codepoint compositions: scan for repeated substrings of length 2..max_n
    for (uint32_t n = 2; n <= config_.max_n; ++n) {
        size_t groups_at_length = 0;
        size_t i = 0;
        while (i < cp_sa.size()) {
            if (cp_sa[i] + n > N) { ++i; continue; }

            // Find group of SA entries sharing the same n-length prefix
            size_t j = i + 1;
            while (j < cp_sa.size() && cp_lcp[j] >= n) ++j;

            uint32_t freq = static_cast<uint32_t>(j - i);
            if (freq >= config_.min_frequency) {
                uint32_t pos = cp_sa[i];
                auto hash = hash_codepoints(text.data() + pos, n);

                auto& ngram = ngrams_[hash];
                if (ngram.frequency == 0) {
                    ngram.text = text.substr(pos, n);
                    ngram.hash = hash;
                    ngram.n = n;
                    
                    // RLE detection
                    bool all_same = true;
                    for (uint32_t k = 1; k < n; ++k) {
                        if (text[pos + k] != text[pos]) { all_same = false; break; }
                    }
                    ngram.is_rle = all_same;
                    
                    // Pattern signature
                    if (n >= 2 && n <= 32) {
                        ngram.pattern_signature = compute_pattern_signature(ngram.text);
                    }
                }
                ngram.frequency = freq;

                // Store positions
                if (config_.track_positions) {
                    ngram.positions.reserve(freq);
                    for (size_t k = i; k < j; ++k) ngram.positions.push_back(cp_sa[k]);
                    std::sort(ngram.positions.begin(), ngram.positions.end());
                }

                // Context for entropy (sampled)
                uint32_t sample_step = std::max(1u, freq / 64);
                for (size_t k = i; k < j; k += sample_step) {
                    uint32_t p = cp_sa[k];
                    if (p > 0) left_context_[hash][text[p - 1]]++;
                    if (p + n < N) right_context_[hash][text[p + n]]++;
                }

                groups_at_length++;
                total_promoted++;
            }
            total_discovered++;
            i = j;
        }
        
        // Early termination: if no compositions found at this length, longer ones won't exist either
        if (groups_at_length == 0) {
            std::cout << "    [sa] no compositions at length " << n << ", stopping" << std::endl;
            break;
        }
    }
    std::cout << "    [sa] composition discovery: " << ms_since(t0) << "ms ("
              << total_discovered << " scanned, " << total_promoted << " promoted, "
              << ngrams_.size() << " total stored)" << std::endl;

    // Free SA/LCP
    { std::vector<uint32_t>().swap(cp_sa); }
    { std::vector<uint32_t>().swap(cp_lcp); }

    // === Phase 5: Finalize metrics (PMI, entropy, branching factor) ===
    t0 = Clock::now();
    finalize_metrics();
    std::cout << "    [sa] finalize metrics: " << ms_since(t0) << "ms" << std::endl;

    std::cout << "    [sa] TOTAL: " << ms_since(t_total) << "ms" << std::endl;
}

double NGramExtractor::calculate_entropy(const std::unordered_map<char32_t, uint32_t>& counts, uint32_t total) {
    if (total == 0) return 0.0;
    double entropy = 0.0;
    for (const auto& [cp, count] : counts) {
        double p = static_cast<double>(count) / total;
        entropy -= p * std::log2(p);
    }
    return entropy;
}

void NGramExtractor::finalize_metrics() {
    if (total_unigrams_ == 0) return;

    for (auto& [hash, ngram] : ngrams_) {
        ngram.left_entropy = calculate_entropy(left_context_[hash], ngram.frequency);
        ngram.right_entropy = calculate_entropy(right_context_[hash], ngram.frequency);
        ngram.branching_factor = static_cast<uint32_t>(right_context_[hash].size());

        if (ngram.n >= 2) {
            auto hash_f = hash_codepoints(&ngram.text[0], 1);
            auto hash_r = hash_codepoints(&ngram.text[1], ngram.n - 1);

            auto it_f = ngrams_.find(hash_f);
            auto it_r = ngrams_.find(hash_r);

            if (it_f != ngrams_.end() && it_r != ngrams_.end()) {
                double p_xy = static_cast<double>(ngram.frequency) / total_unigrams_;
                double p_x = static_cast<double>(it_f->second.frequency) / total_unigrams_;
                double p_y = static_cast<double>(it_r->second.frequency) / total_unigrams_;
                if (p_x > 0 && p_y > 0 && p_xy > 0) {
                    ngram.pmi = std::log2(p_xy / (p_x * p_y));
                    double log_pxy = -std::log2(p_xy);
                    ngram.npmi = (log_pxy > 0) ? ngram.pmi / log_pxy : 0.0;
                }
            }
        }
    }
    left_context_.clear();
    right_context_.clear();
}

std::vector<const NGram*> NGramExtractor::significant_ngrams() const {
    std::vector<const NGram*> result;
    for (const auto& [hash, ngram] : ngrams_) {
        if (ngram.n == 1) { result.push_back(&ngram); continue; }

        bool sig = ngram.frequency >= config_.min_frequency &&
                   ngram.npmi >= config_.min_npmi &&
                   (ngram.left_entropy >= config_.min_entropy || ngram.right_entropy >= config_.min_entropy) &&
                   ngram.branching_factor <= config_.max_branching_factor;

        if (sig || ngram.is_rle) result.push_back(&ngram);
    }
    std::sort(result.begin(), result.end(), [](const NGram* a, const NGram* b) {
        if (a->n != b->n) return a->n > b->n;  // Longer compositions first
        if (a->frequency != b->frequency) return a->frequency > b->frequency;
        return a->npmi > b->npmi;
    });
    return result;
}

void NGramExtractor::clear() {
    ngrams_.clear();
    total_unigrams_ = 0;
}

}