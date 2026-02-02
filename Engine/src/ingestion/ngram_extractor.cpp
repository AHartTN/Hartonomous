/**
 * @file ngram_extractor.cpp
 * @brief N-gram extraction implementation with statistical grounding
 */

#include <ingestion/ngram_extractor.hpp>
#include <algorithm>
#include <cmath>
#include <deque>

namespace Hartonomous {

NGramExtractor::NGramExtractor(const NGramConfig& config) : config_(config) {}

void NGramExtractor::extract(const std::u32string& text) {
    if (text.empty()) return;

    struct ActiveNGram {
        BLAKE3Pipeline::Hash hash;
        uint32_t position;
    };
    std::deque<ActiveNGram> window;

    for (uint32_t pos = 0; pos < text.size(); ++pos) {
        std::vector<BLAKE3Pipeline::Hash> current_pos_hashes;
        
        // Rolling BLAKE3 feed for this starting position
        blake3_hasher hasher;
        blake3_hasher_init(&hasher);

        for (uint32_t n = 1; n <= config_.max_n && pos + n <= text.size(); ++n) {
            char32_t cp = text[pos + n - 1];
            uint8_t bytes[4] = {
                static_cast<uint8_t>(cp & 0xFF),
                static_cast<uint8_t>((cp >> 8) & 0xFF),
                static_cast<uint8_t>((cp >> 16) & 0xFF),
                static_cast<uint8_t>((cp >> 24) & 0xFF)
            };
            blake3_hasher_update(&hasher, bytes, 4);

            if (n < config_.min_n) continue;

            // Clone hasher to finalize this specific n-gram length without disturbing the rolling state
            blake3_hasher hasher_clone;
            std::memcpy(&hasher_clone, &hasher, sizeof(blake3_hasher));
            
            BLAKE3Pipeline::Hash hash;
            blake3_hasher_finalize(&hasher_clone, hash.data(), BLAKE3Pipeline::HASH_SIZE);
            current_pos_hashes.push_back(hash);

            auto& ngram = ngrams_[hash];
            if (ngram.frequency == 0) {
                ngram.text = text.substr(pos, n);
                ngram.hash = hash;
                ngram.n = n;
                if (n > 1) {
                    bool all_same = true;
                    for (size_t i = 1; i < ngram.text.size(); ++i) {
                        if (ngram.text[i] != ngram.text[0]) { all_same = false; break; }
                    }
                    ngram.is_rle = all_same;
                }
            }
            ngram.frequency++;
            if (config_.track_positions) ngram.positions.push_back(pos);
            if (n == 1) total_unigrams_++;

            if (pos > 0) left_context_[hash][text[pos-1]]++;
            if (pos + n < text.size()) right_context_[hash][text[pos+n]]++;

            for (const auto& active : window) {
                if (active.hash != hash) record_cooccurrence(active.hash, hash, active.position, pos);
            }
        }

        for (const auto& h : current_pos_hashes) window.push_back({h, pos});
        while (!window.empty() && window.front().position + config_.cooccurrence_window < pos) {
            window.pop_front();
        }
    }
    finalize_metrics();
}

void NGramExtractor::record_cooccurrence(const BLAKE3Pipeline::Hash& h1, const BLAKE3Pipeline::Hash& h2, uint32_t p1, uint32_t p2) {
    bool a_first = h1 < h2;
    auto key = a_first ? std::make_pair(h1, h2) : std::make_pair(h2, h1);
    auto& cooc = cooccurrences_[key];

    if (cooc.count == 0) {
        cooc.ngram_a = key.first;
        cooc.ngram_b = key.second;
    }

    uint32_t dist = (p2 > p1) ? (p2 - p1) : (p1 - p2);
    double old_avg = cooc.avg_distance;
    cooc.count++;
    cooc.avg_distance = old_avg + (static_cast<double>(dist) - old_avg) / cooc.count;

    if (config_.track_direction) {
        cooc.direction_sum += (a_first == (p1 < p2)) ? 1 : -1;
    }
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
            // Re-hash components incrementally for PMI
            blake3_hasher h_f; blake3_hasher_init(&h_f);
            char32_t cp_f = ngram.text[0];
            uint8_t b_f[4] = { uint8_t(cp_f), uint8_t(cp_f >> 8), uint8_t(cp_f >> 16), uint8_t(cp_f >> 24) };
            blake3_hasher_update(&h_f, b_f, 4);
            BLAKE3Pipeline::Hash hash_f; blake3_hasher_finalize(&h_f, hash_f.data(), BLAKE3Pipeline::HASH_SIZE);

            blake3_hasher h_r; blake3_hasher_init(&h_r);
            for (size_t i = 1; i < ngram.text.size(); ++i) {
                char32_t cp = ngram.text[i];
                uint8_t bytes[4] = { uint8_t(cp), uint8_t(cp >> 8), uint8_t(cp >> 16), uint8_t(cp >> 24) };
                blake3_hasher_update(&h_r, bytes, 4);
            }
            BLAKE3Pipeline::Hash hash_r; blake3_hasher_finalize(&h_r, hash_r.data(), BLAKE3Pipeline::HASH_SIZE);
            
            auto it_f = ngrams_.find(hash_f);
            auto it_r = ngrams_.find(hash_r);
            
            if (it_f != ngrams_.end() && it_r != ngrams_.end()) {
                double p_xy = static_cast<double>(ngram.frequency) / total_unigrams_;
                double p_x = static_cast<double>(it_f->second.frequency) / total_unigrams_;
                double p_y = static_cast<double>(it_r->second.frequency) / total_unigrams_;
                ngram.pmi = std::log2(p_xy / (p_x * p_y));
                ngram.npmi = ngram.pmi / (-std::log2(p_xy));
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
        if (a->frequency != b->frequency) return a->frequency > b->frequency;
        return a->npmi > b->npmi;
    });
    return result;
}

std::vector<const CoOccurrence*> NGramExtractor::significant_cooccurrences(uint32_t min_count) const {
    std::vector<const CoOccurrence*> result;
    for (const auto& [key, cooc] : cooccurrences_) if (cooc.count >= min_count) result.push_back(&cooc);
    std::sort(result.begin(), result.end(), [](const CoOccurrence* a, const CoOccurrence* b) {
        return a->count > b->count;
    });
    return result;
}

void NGramExtractor::clear() {
    ngrams_.clear();
    cooccurrences_.clear();
    total_unigrams_ = 0;
}

}