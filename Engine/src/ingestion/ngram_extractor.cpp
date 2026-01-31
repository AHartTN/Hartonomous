/**
 * @file ngram_extractor.cpp
 * @brief N-gram extraction implementation
 */

#include <ingestion/ngram_extractor.hpp>
#include <algorithm>
#include <cmath>
#include <iomanip>
#include <sstream>

namespace Hartonomous {

NGramExtractor::NGramExtractor(const NGramConfig& config) : config_(config) {}

std::string NGramExtractor::hash_to_hex(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (size_t i = 0; i < hash.size(); ++i) {
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

BLAKE3Pipeline::Hash NGramExtractor::compute_hash(const std::u32string& text) {
    // Convert UTF-32 to bytes for hashing
    std::vector<uint8_t> data;
    data.reserve(text.size() * 4);
    for (char32_t cp : text) {
        // Little-endian encoding of codepoint
        data.push_back(static_cast<uint8_t>(cp & 0xFF));
        data.push_back(static_cast<uint8_t>((cp >> 8) & 0xFF));
        data.push_back(static_cast<uint8_t>((cp >> 16) & 0xFF));
        data.push_back(static_cast<uint8_t>((cp >> 24) & 0xFF));
    }
    return BLAKE3Pipeline::hash(data);
}

void NGramExtractor::extract(const std::u32string& text) {
    if (text.empty()) return;

    // Phase 1: Extract all n-grams and count frequencies
    // Store positions for co-occurrence computation
    struct NGramPosition {
        std::string hash_hex;
        BLAKE3Pipeline::Hash full_hash;
        uint32_t position;
        uint32_t n;
    };
    std::vector<NGramPosition> all_positions;

    for (uint32_t n = config_.min_n; n <= config_.max_n && n <= text.size(); ++n) {
        for (uint32_t pos = 0; pos + n <= text.size(); ++pos) {
            std::u32string ngram_text = text.substr(pos, n);
            auto hash = compute_hash(ngram_text);
            std::string hash_hex = hash_to_hex(hash);

            // Update or create n-gram entry
            auto& ngram = ngrams_[hash_hex];
            if (ngram.frequency == 0) {
                // New n-gram
                ngram.text = ngram_text;
                ngram.hash = hash;
                ngram.n = n;
            }
            ngram.frequency++;

            if (config_.track_positions) {
                ngram.positions.push_back(pos);
            }

            // Record position for co-occurrence computation
            all_positions.push_back({hash_hex, hash, pos, n});
        }
    }

    // Phase 2: Compute co-occurrences within window
    // For efficiency, we process positions in order
    for (size_t i = 0; i < all_positions.size(); ++i) {
        const auto& pos_a = all_positions[i];

        // Look ahead within window
        for (size_t j = i + 1; j < all_positions.size(); ++j) {
            const auto& pos_b = all_positions[j];

            // Check if still within window (based on starting position)
            uint32_t distance = (pos_b.position > pos_a.position)
                ? (pos_b.position - pos_a.position)
                : (pos_a.position - pos_b.position);

            if (distance > config_.cooccurrence_window) {
                // If positions are sorted, we can break early for this starting position
                // But n-grams of different sizes at same position might still be in window
                if (pos_b.position > pos_a.position + config_.cooccurrence_window) {
                    break;
                }
                continue;
            }

            // Skip self-comparisons (same hash)
            if (pos_a.hash_hex == pos_b.hash_hex) continue;

            // Record co-occurrence
            record_cooccurrence(
                pos_a.hash_hex, pos_a.full_hash,
                pos_b.hash_hex, pos_b.full_hash,
                pos_a.position, pos_b.position
            );
        }
    }
}

void NGramExtractor::record_cooccurrence(
    const std::string& hash_a, const BLAKE3Pipeline::Hash& full_hash_a,
    const std::string& hash_b, const BLAKE3Pipeline::Hash& full_hash_b,
    uint32_t pos_a, uint32_t pos_b) {

    // Normalize order: always store (smaller, larger) for consistency
    bool a_first = hash_a < hash_b;
    const std::string& first = a_first ? hash_a : hash_b;
    const std::string& second = a_first ? hash_b : hash_a;

    auto key = std::make_pair(first, second);
    auto& cooc = cooccurrences_[key];

    // Initialize hashes if new
    if (cooc.count == 0) {
        cooc.ngram_a = a_first ? full_hash_a : full_hash_b;
        cooc.ngram_b = a_first ? full_hash_b : full_hash_a;
    }

    // Update statistics
    uint32_t distance = (pos_b > pos_a) ? (pos_b - pos_a) : (pos_a - pos_b);
    double old_avg = cooc.avg_distance;
    cooc.count++;
    // Incremental average update
    cooc.avg_distance = old_avg + (static_cast<double>(distance) - old_avg) / cooc.count;

    // Track direction relative to normalized order
    if (config_.track_direction) {
        // If A (first in normalized pair) appears before B in text, +1
        // If A appears after B in text, -1
        if (a_first) {
            cooc.direction_sum += (pos_a < pos_b) ? 1 : -1;
        } else {
            // hash_a is actually second in our storage
            cooc.direction_sum += (pos_b < pos_a) ? 1 : -1;
        }
    }
}

std::vector<const NGram*> NGramExtractor::significant_ngrams() const {
    std::vector<const NGram*> result;
    for (const auto& [hash, ngram] : ngrams_) {
        if (ngram.frequency >= config_.min_frequency) {
            result.push_back(&ngram);
        }
    }

    // Sort by frequency (descending)
    std::sort(result.begin(), result.end(),
        [](const NGram* a, const NGram* b) {
            return a->frequency > b->frequency;
        });

    return result;
}

std::vector<const CoOccurrence*> NGramExtractor::significant_cooccurrences(uint32_t min_count) const {
    std::vector<const CoOccurrence*> result;
    for (const auto& [key, cooc] : cooccurrences_) {
        if (cooc.count >= min_count) {
            result.push_back(&cooc);
        }
    }

    // Sort by count (descending)
    std::sort(result.begin(), result.end(),
        [](const CoOccurrence* a, const CoOccurrence* b) {
            return a->count > b->count;
        });

    return result;
}

void NGramExtractor::clear() {
    ngrams_.clear();
    cooccurrences_.clear();
}

}
