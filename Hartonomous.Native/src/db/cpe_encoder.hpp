#pragma once

/// CPE ENCODER - Cascading Pair Encoding for Unicode codepoints.
///
/// Implements O(n log n) BPE algorithm:
/// Per level: O(n) pair count → O(n) stream rewrite
/// Stream shrinks exponentially → ~log(n) levels
/// Total: O(n log n)
///
/// Extracted from QueryStore for separation of concerns.

#include "../atoms/node_ref.hpp"
#include "../atoms/codepoint_atom_table.hpp"
#include "../atoms/merkle_hash.hpp"
#include <vector>
#include <unordered_map>
#include <algorithm>
#include <cmath>

namespace hartonomous::db {

/// Atom with position for CPE processing.
struct CpeAtom {
    NodeRef ref;
    double x, y, z, m;  // Semantic coordinates
};

/// Pair statistics for frequency counting.
struct PairStats {
    NodeRef left, right;
    std::uint32_t count = 0;
    double sum_dist = 0.0;  // Sum of distances for semantic coherence
};

/// CPE Encoder - Handles all Cascading Pair Encoding operations.
class CpeEncoder {
    // CPE frequency threshold - pairs must occur this many times to become compositions
    static constexpr std::uint32_t CPE_FREQUENCY_THRESHOLD = 2;
    
    // Maximum hierarchy depth (Z levels)
    static constexpr double CPE_MAX_Z_LEVEL = 30.0;

    // Merge table: maps (left, right) pair keys → composition NodeRef
    // Built during ingestion, used for consistent query encoding
    std::unordered_map<std::uint64_t, NodeRef> merge_table_;

    static std::uint64_t make_key(std::int64_t high, std::int64_t low) noexcept {
        return static_cast<std::uint64_t>(high) ^
               (static_cast<std::uint64_t>(low) * 0x9e3779b97f4a7c15ULL);
    }

public:
    /// Create pair key for HashMap lookup.
    static std::uint64_t make_pair_key(const NodeRef& left, const NodeRef& right) noexcept {
        std::uint64_t lk = make_key(left.id_high, left.id_low);
        std::uint64_t rk = make_key(right.id_high, right.id_low);
        return lk ^ (rk * 0x9e3779b97f4a7c15ULL);
    }

    /// Apply RLE compression - collapse runs of identical atoms.
    static std::vector<CpeAtom> apply_rle(const std::vector<CpeAtom>& atoms) {
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
    std::unordered_map<std::uint64_t, PairStats> count_pairs(const std::vector<CpeAtom>& stream) {
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
    std::vector<std::pair<std::uint64_t, PairStats>> get_frequent_pairs(
        const std::unordered_map<std::uint64_t, PairStats>& pair_counts)
    {
        std::vector<std::pair<std::uint64_t, PairStats>> frequent;
        frequent.reserve(pair_counts.size());
        
        for (const auto& [key, stats] : pair_counts) {
            if (stats.count >= CPE_FREQUENCY_THRESHOLD) {
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
    std::vector<CpeAtom> rewrite_stream(
        const std::vector<CpeAtom>& stream,
        const std::unordered_map<std::uint64_t, NodeRef>& pair_to_comp,
        double z_level)
    {
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

    /// Initialize stream with codepoint atoms.
    std::vector<CpeAtom> init_stream(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end)
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::vector<CpeAtom> stream;
        stream.reserve(end - start);
        
        for (std::size_t i = start; i < end; ++i) {
            CpeAtom atom;
            atom.ref = atoms.ref(codepoints[i]);
            atom.x = static_cast<double>(i - start);
            atom.y = 0.0;
            atom.z = 0.0;
            atom.m = 1.0;
            stream.push_back(atom);
        }
        
        return stream;
    }

    /// Compute CPE hash WITHOUT storing - for content addressing.
    [[nodiscard]] NodeRef compute_cpe_hash(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end) const
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::size_t len = end - start;

        if (len == 0) return NodeRef{};
        if (len == 1) return atoms.ref(codepoints[start]);

        // Initialize stream with atoms
        std::vector<CpeAtom> stream;
        stream.reserve(len);
        
        for (std::size_t i = start; i < end; ++i) {
            CpeAtom atom;
            atom.ref = atoms.ref(codepoints[i]);
            atom.x = static_cast<double>(i - start);
            atom.y = 0.0;
            atom.z = 0.0;
            atom.m = 1.0;
            stream.push_back(atom);
        }
        
        double z_level = 1.0;
        std::size_t prev_size = 0;
        
        // Process levels until convergence
        while (stream.size() >= 2 && z_level <= CPE_MAX_Z_LEVEL) {
            if (stream.size() < 2) break;
            
            // Check for convergence
            if (prev_size > 0) {
                double ratio = static_cast<double>(stream.size()) / static_cast<double>(prev_size);
                if (ratio > 0.999) break;
            }
            prev_size = stream.size();
            
            // Count pairs - O(n)
            std::unordered_map<std::uint64_t, PairStats> pair_counts;
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
                
                double dx = right.x - left.x;
                double dy = right.y - left.y;
                stats.sum_dist += std::sqrt(dx * dx + dy * dy);
            }
            
            // Get frequent pairs
            std::vector<std::pair<std::uint64_t, PairStats>> frequent;
            frequent.reserve(pair_counts.size());
            
            for (const auto& [key, stats] : pair_counts) {
                if (stats.count >= CPE_FREQUENCY_THRESHOLD) {
                    frequent.emplace_back(key, stats);
                }
            }
            
            if (frequent.empty()) break;
            
            // Sort by score
            std::sort(frequent.begin(), frequent.end(),
                [](const auto& a, const auto& b) {
                    double avg_dist_a = a.second.sum_dist / a.second.count;
                    double avg_dist_b = b.second.sum_dist / b.second.count;
                    double score_a = a.second.count / (avg_dist_a + 1.0);
                    double score_b = b.second.count / (avg_dist_b + 1.0);
                    return score_a > score_b;
                });
            
            // Create compositions and build lookup map
            std::unordered_map<std::uint64_t, NodeRef> pair_to_comp;
            
            for (const auto& [key, stats] : frequent) {
                NodeRef children[2] = {stats.left, stats.right};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                pair_to_comp[key] = comp;
            }
            
            // Rewrite stream - O(n)
            std::vector<CpeAtom> result;
            result.reserve(stream.size());
            
            std::size_t i = 0;
            while (i < stream.size()) {
                if (i + 1 < stream.size()) {
                    std::uint64_t key = make_pair_key(stream[i].ref, stream[i + 1].ref);
                    auto it = pair_to_comp.find(key);
                    
                    if (it != pair_to_comp.end()) {
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
            
            stream = std::move(result);
            z_level += 1.0;
        }
        
        // Combine remaining atoms with binary tree
        while (stream.size() > 1) {
            std::vector<CpeAtom> next_level;
            next_level.reserve((stream.size() + 1) / 2);
            
            for (std::size_t i = 0; i + 1 < stream.size(); i += 2) {
                NodeRef children[2] = {stream[i].ref, stream[i + 1].ref};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                
                CpeAtom merged;
                merged.ref = comp;
                merged.x = (stream[i].x + stream[i + 1].x) / 2.0;
                merged.y = (stream[i].y + stream[i + 1].y) / 2.0;
                merged.z = z_level;
                merged.m = stream[i].m + stream[i + 1].m;
                next_level.push_back(merged);
            }
            
            if (stream.size() % 2 == 1) {
                next_level.push_back(stream.back());
            }
            
            stream = std::move(next_level);
            z_level += 1.0;
        }
        
        return stream.empty() ? NodeRef{} : stream[0].ref;
    }

    /// Encode a query using the pre-built merge table from ingestion.
    [[nodiscard]] NodeRef encode_with_merge_table(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end) const
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::size_t len = end - start;
        
        if (len == 0) return NodeRef{};
        if (len == 1) return atoms.ref(codepoints[start]);
        
        // Initialize stream with atoms
        std::vector<NodeRef> stream;
        stream.reserve(len);
        
        for (std::size_t i = start; i < end; ++i) {
            stream.push_back(atoms.ref(codepoints[i]));
        }
        
        // Apply merges from the table until no more matches
        bool made_progress = true;
        while (made_progress && stream.size() > 1) {
            made_progress = false;
            std::vector<NodeRef> next_stream;
            next_stream.reserve(stream.size());
            
            std::size_t i = 0;
            while (i < stream.size()) {
                if (i + 1 < stream.size()) {
                    std::uint64_t key = make_pair_key(stream[i], stream[i + 1]);
                    auto it = merge_table_.find(key);
                    
                    if (it != merge_table_.end()) {
                        next_stream.push_back(it->second);
                        i += 2;
                        made_progress = true;
                        continue;
                    }
                }
                next_stream.push_back(stream[i]);
                i++;
            }
            
            stream = std::move(next_stream);
        }
        
        // Create binary tree for remaining elements
        while (stream.size() > 1) {
            std::vector<NodeRef> next_level;
            next_level.reserve((stream.size() + 1) / 2);
            
            for (std::size_t i = 0; i + 1 < stream.size(); i += 2) {
                NodeRef children[2] = {stream[i], stream[i + 1]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                next_level.push_back(NodeRef::comp(h, l));
            }
            
            if (stream.size() % 2 == 1) {
                next_level.push_back(stream.back());
            }
            
            stream = std::move(next_level);
        }
        
        return stream.empty() ? NodeRef{} : stream[0];
    }

    /// Build CPE and collect compositions for storage.
    /// Populates pending_compositions and merge_table.
    NodeRef build_cpe_and_collect(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end,
        std::vector<std::tuple<NodeRef, NodeRef, NodeRef>>& pending_compositions)
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::size_t len = end - start;
        
        if (len == 0) return NodeRef{};
        if (len == 1) return atoms.ref(codepoints[start]);
        
        // Initialize stream with atoms
        std::vector<CpeAtom> stream = init_stream(codepoints, start, end);
        
        double z_level = 1.0;
        std::size_t prev_size = 0;
        
        // Process levels until convergence
        while (stream.size() >= 2 && z_level <= CPE_MAX_Z_LEVEL) {
            if (stream.size() < 2) break;
            
            // Check for convergence
            if (prev_size > 0) {
                double ratio = static_cast<double>(stream.size()) / static_cast<double>(prev_size);
                if (ratio > 0.999) break;
            }
            prev_size = stream.size();
            
            // Count pairs - O(n)
            auto pair_counts = count_pairs(stream);
            
            // Get frequent pairs above threshold, sorted by score
            auto frequent_pairs = get_frequent_pairs(pair_counts);
            
            if (frequent_pairs.empty()) break;
            
            // Create compositions and build lookup map
            std::unordered_map<std::uint64_t, NodeRef> pair_to_comp;
            
            for (const auto& [key, stats] : frequent_pairs) {
                NodeRef children[2] = {stats.left, stats.right};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                
                // Store composition
                pending_compositions.emplace_back(comp, stats.left, stats.right);
                pair_to_comp[key] = comp;
                
                // Add to global merge table for query encoding
                merge_table_[key] = comp;
            }
            
            // Rewrite stream - O(n)
            stream = rewrite_stream(stream, pair_to_comp, z_level);
            z_level += 1.0;
        }
        
        // Create final composition chain for remaining atoms
        while (stream.size() > 1) {
            std::vector<CpeAtom> next_level;
            next_level.reserve((stream.size() + 1) / 2);
            
            for (std::size_t i = 0; i + 1 < stream.size(); i += 2) {
                NodeRef children[2] = {stream[i].ref, stream[i + 1].ref};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                pending_compositions.emplace_back(comp, stream[i].ref, stream[i + 1].ref);
                
                CpeAtom merged;
                merged.ref = comp;
                merged.x = (stream[i].x + stream[i + 1].x) / 2.0;
                merged.y = (stream[i].y + stream[i + 1].y) / 2.0;
                merged.z = z_level;
                merged.m = stream[i].m + stream[i + 1].m;
                next_level.push_back(merged);
            }
            
            if (stream.size() % 2 == 1) {
                next_level.push_back(stream.back());
            }
            
            stream = std::move(next_level);
            z_level += 1.0;
        }
        
        return stream.empty() ? NodeRef{} : stream[0].ref;
    }

    /// Access the merge table.
    const std::unordered_map<std::uint64_t, NodeRef>& merge_table() const {
        return merge_table_;
    }

    /// Add entry to merge table.
    void add_merge(std::uint64_t key, NodeRef comp) {
        merge_table_[key] = comp;
    }

    /// Get CPE frequency threshold.
    static constexpr std::uint32_t frequency_threshold() {
        return CPE_FREQUENCY_THRESHOLD;
    }

    /// Get max Z level.
    static constexpr double max_z_level() {
        return CPE_MAX_Z_LEVEL;
    }
};

} // namespace hartonomous::db
