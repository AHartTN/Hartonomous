#pragma once

/// CPE ENCODER - Content-Pair Encoding algorithm implementation.
///
/// CPE is a variant of Byte-Pair Encoding optimized for semantic coherence.
/// It uses frequency × semantic distance scoring to find the best pairs.
///
/// Complexity: O(n log n) where n is input length.
/// - Per level: O(n) pair count → O(n) stream rewrite
/// - Stream shrinks exponentially → ~log(n) levels
///
/// Extracted from query_store.hpp for separation of concerns.

#include "../atoms/node_ref.hpp"
#include "../atoms/merkle_hash.hpp"
#include "../atoms/codepoint_atom_table.hpp"
#include "../atoms/semantic_decompose.hpp"
#include <vector>
#include <unordered_map>
#include <algorithm>
#include <cstdint>
#include <cmath>
#include <utility>

namespace hartonomous::atoms {

/// Atom with position for CPE processing.
/// Carries semantic coordinates for distance calculations.
struct CpeAtom {
    NodeRef ref;
    double x, y, z, m;  // Semantic coordinates
};

/// Pair statistics for frequency counting.
/// Tracks count and distance sum for coherence scoring.
struct PairStats {
    NodeRef left, right;
    std::uint32_t count = 0;
    double sum_dist = 0.0;  // Sum of distances for semantic coherence
};

/// CPE frequency threshold - pairs must occur this many times to become compositions.
constexpr std::uint32_t CPE_FREQUENCY_THRESHOLD = 2;

/// Maximum hierarchy depth (Z levels).
constexpr double CPE_MAX_Z_LEVEL = 30.0;

/// Content-Pair Encoding engine.
/// Implements O(n log n) BPE variant with semantic coherence scoring.
class CpeEncoder {
public:
    /// Create hash key from high/low components.
    [[nodiscard]] static std::uint64_t make_key(std::int64_t high, std::int64_t low) noexcept {
        return static_cast<std::uint64_t>(high) ^
               (static_cast<std::uint64_t>(low) * 0x9e3779b97f4a7c15ULL);
    }

    /// Create pair key for HashMap lookup.
    [[nodiscard]] static std::uint64_t make_pair_key(const NodeRef& left, const NodeRef& right) noexcept {
        std::uint64_t lk = make_key(left.id_high, left.id_low);
        std::uint64_t rk = make_key(right.id_high, right.id_low);
        return lk ^ (rk * 0x9e3779b97f4a7c15ULL);
    }

    /// Apply RLE compression - collapse runs of identical atoms.
    [[nodiscard]] static std::vector<CpeAtom> apply_rle(const std::vector<CpeAtom>& atoms) {
        if (atoms.empty()) return {};

        std::vector<CpeAtom> compressed;
        compressed.reserve(atoms.size());

        CpeAtom current = atoms[0];
        double run_length = 1.0;

        for (std::size_t i = 1; i < atoms.size(); ++i) {
            if (atoms[i].ref.id_high == current.ref.id_high &&
                atoms[i].ref.id_low == current.ref.id_low) {
                run_length += 1.0;
            } else {
                current.m = run_length;
                compressed.push_back(current);
                current = atoms[i];
                run_length = 1.0;
            }
        }
        current.m = run_length;
        compressed.push_back(current);

        return compressed;
    }

    /// Count pairs in stream - O(n) single pass.
    [[nodiscard]] static std::unordered_map<std::uint64_t, PairStats> count_pairs(
        const std::vector<CpeAtom>& stream) {
        std::unordered_map<std::uint64_t, PairStats> pair_counts;
        if (stream.size() < 2) return pair_counts;

        pair_counts.reserve(stream.size() / 2);

        for (std::size_t i = 0; i + 1 < stream.size(); ++i) {
            const auto& left = stream[i];
            const auto& right = stream[i + 1];

            std::uint64_t key = make_pair_key(left.ref, right.ref);
            auto& stats = pair_counts[key];

            if (stats.count == 0) {
                stats.left = left.ref;
                stats.right = right.ref;
            }
            stats.count++;

            // Semantic distance in XY plane
            double dx = right.x - left.x;
            double dy = right.y - left.y;
            stats.sum_dist += std::sqrt(dx * dx + dy * dy);
        }

        return pair_counts;
    }

    /// Find best pairs above threshold, sorted by score.
    [[nodiscard]] static std::vector<std::pair<std::uint64_t, PairStats>> get_frequent_pairs(
        const std::unordered_map<std::uint64_t, PairStats>& pair_counts,
        std::uint32_t threshold = CPE_FREQUENCY_THRESHOLD) {
        std::vector<std::pair<std::uint64_t, PairStats>> frequent;
        frequent.reserve(pair_counts.size());

        for (const auto& [key, stats] : pair_counts) {
            if (stats.count >= threshold) {
                frequent.emplace_back(key, stats);
            }
        }

        // Sort by score: frequency * coherence (low distance = high coherence)
        std::sort(frequent.begin(), frequent.end(),
            [](const auto& a, const auto& b) {
                double avg_dist_a = a.second.sum_dist / a.second.count;
                double avg_dist_b = b.second.sum_dist / b.second.count;
                double score_a = a.second.count / (avg_dist_a + 1.0);
                double score_b = b.second.count / (avg_dist_b + 1.0);
                return score_a > score_b;
            });

        return frequent;
    }

    /// Rewrite stream - replace all matched pairs with compositions - O(n).
    [[nodiscard]] static std::vector<CpeAtom> rewrite_stream(
        const std::vector<CpeAtom>& stream,
        const std::unordered_map<std::uint64_t, NodeRef>& pair_to_comp,
        double z_level) {
        std::vector<CpeAtom> result;
        result.reserve(stream.size());

        std::size_t i = 0;
        while (i < stream.size()) {
            if (i + 1 < stream.size()) {
                std::uint64_t key = make_pair_key(stream[i].ref, stream[i + 1].ref);
                auto it = pair_to_comp.find(key);

                if (it != pair_to_comp.end()) {
                    // Replace pair with composition
                    CpeAtom comp_atom;
                    comp_atom.ref = it->second;
                    comp_atom.x = (stream[i].x + stream[i + 1].x) / 2.0;
                    comp_atom.y = (stream[i].y + stream[i + 1].y) / 2.0;
                    comp_atom.z = z_level;
                    comp_atom.m = stream[i].m + stream[i + 1].m;
                    result.push_back(comp_atom);
                    i += 2;
                    continue;
                }
            }
            result.push_back(stream[i]);
            i++;
        }

        return result;
    }

    /// Convert codepoints to initial CpeAtom stream.
    [[nodiscard]] static std::vector<CpeAtom> codepoints_to_atoms(
        const std::vector<std::int32_t>& codepoints) {
        const auto& atom_table = CodepointAtomTable::instance();

        std::vector<CpeAtom> stream;
        stream.reserve(codepoints.size());

        for (std::int32_t cp : codepoints) {
            auto atom_id = atom_table.lookup(cp);
            auto coord = SemanticDecompose::get_coord(cp);

            CpeAtom atom;
            atom.ref = NodeRef::atom(atom_id.high, atom_id.low);
            atom.x = static_cast<double>(coord.page);
            atom.y = static_cast<double>(coord.type);
            atom.z = 0.0;  // Base level
            atom.m = 1.0;  // Initial weight
            stream.push_back(atom);
        }

        return stream;
    }

    /// Compute CPE hash - deterministic root for content addressing.
    /// This is the hash-only version (no storage).
    [[nodiscard]] static NodeRef compute_hash(
        const std::vector<std::int32_t>& codepoints,
        std::unordered_map<std::uint64_t, NodeRef>& merge_table) {
        if (codepoints.empty()) return NodeRef{};

        auto stream = codepoints_to_atoms(codepoints);

        // Apply RLE first
        stream = apply_rle(stream);

        if (stream.size() == 1) {
            return stream[0].ref;
        }

        double z_level = 1.0;

        // CPE iterations: count pairs, create compositions, rewrite
        while (stream.size() > 1 && z_level < CPE_MAX_Z_LEVEL) {
            std::size_t prev_size = stream.size();

            auto pair_counts = count_pairs(stream);
            auto frequent = get_frequent_pairs(pair_counts);

            if (frequent.empty()) {
                // No frequent pairs - check for convergence
                double ratio = static_cast<double>(stream.size()) / static_cast<double>(prev_size);
                if (ratio > 0.95) break;  // Not making progress

                // Force single merge of adjacent pairs
                for (std::size_t i = 0; i + 1 < stream.size(); ++i) {
                    const auto& left = stream[i];
                    const auto& right = stream[i + 1];

                    std::uint64_t key = make_pair_key(left.ref, right.ref);
                    auto& stats = pair_counts[key];
                    if (stats.count == 0) {
                        stats.left = left.ref;
                        stats.right = right.ref;
                    }
                    stats.count++;

                    double dx = right.x - left.x;
                    double dy = right.y - left.y;
                    stats.sum_dist += std::sqrt(dx * dx + dy * dy);
                }
                frequent = get_frequent_pairs(pair_counts, 1);  // Lower threshold
            }

            // Create compositions for frequent pairs
            std::unordered_map<std::uint64_t, NodeRef> pair_to_comp;
            for (const auto& [key, stats] : frequent) {
                NodeRef children[2] = {stats.left, stats.right};
                auto [h, l] = MerkleHash::hash_children(children, 2);
                NodeRef comp = NodeRef::comp(h, l);
                pair_to_comp[key] = comp;
                merge_table[key] = comp;
            }

            if (pair_to_comp.empty()) break;

            // Rewrite stream
            stream = rewrite_stream(stream, pair_to_comp, z_level);
            z_level += 1.0;
        }

        // Final reduction: balanced binary tree
        while (stream.size() > 1) {
            std::vector<CpeAtom> next;
            next.reserve((stream.size() + 1) / 2);

            for (std::size_t i = 0; i + 1 < stream.size(); i += 2) {
                NodeRef children[2] = {stream[i].ref, stream[i + 1].ref};
                auto [h, l] = MerkleHash::hash_children(children, 2);
                NodeRef comp = NodeRef::comp(h, l);

                CpeAtom atom;
                atom.ref = comp;
                atom.x = (stream[i].x + stream[i + 1].x) / 2.0;
                atom.y = (stream[i].y + stream[i + 1].y) / 2.0;
                atom.z = z_level;
                atom.m = stream[i].m + stream[i + 1].m;
                next.push_back(atom);

                std::uint64_t key = make_pair_key(stream[i].ref, stream[i + 1].ref);
                merge_table[key] = comp;
            }

            if (stream.size() % 2 == 1) {
                next.push_back(stream.back());
            }

            stream = std::move(next);
            z_level += 1.0;
        }

        return stream.empty() ? NodeRef{} : stream[0].ref;
    }
};

} // namespace hartonomous::atoms
