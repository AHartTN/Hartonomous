#pragma once

/// GRAMMAR-BASED ENCODER (Sequitur-inspired)
///
/// Language-agnostic encoding that discovers patterns in byte sequences.
/// Unlike "split on spaces" bullshit, this works on:
/// - Chinese (no spaces)
/// - Japanese (mixed scripts)
/// - Binary data
/// - Any byte sequence
///
/// Algorithm:
/// 1. Scan for repeated bigrams (pairs of symbols)
/// 2. Create grammar rule for most frequent bigram
/// 3. Replace all occurrences with rule reference
/// 4. Repeat until no beneficial patterns remain
///
/// This creates a hierarchical grammar that IS the CPE tree.
/// Same content = same grammar = same composition roots.

#include "node_ref.hpp"
#include "byte_atom_table.hpp"
#include "merkle_hash.hpp"
#include <vector>
#include <unordered_map>
#include <algorithm>

namespace hartonomous {

/// Grammar rule: represents a composition
struct GrammarRule {
    NodeRef ref;        // This rule's NodeRef
    NodeRef left;       // Left child (atom or rule)
    NodeRef right;      // Right child (atom or rule)
    std::uint32_t freq; // Frequency in sequence
};

/// Symbol in sequence: either atom or rule reference
struct Symbol {
    NodeRef ref;
    bool is_atom;

    bool operator==(const Symbol& o) const {
        return ref.id_high == o.ref.id_high &&
               ref.id_low == o.ref.id_low &&
               is_atom == o.is_atom;
    }
};

struct SymbolPairHash {
    std::size_t operator()(const std::pair<Symbol, Symbol>& p) const {
        std::size_t h1 = static_cast<std::size_t>(p.first.ref.id_high) ^
                         (static_cast<std::size_t>(p.first.ref.id_low) << 32);
        std::size_t h2 = static_cast<std::size_t>(p.second.ref.id_high) ^
                         (static_cast<std::size_t>(p.second.ref.id_low) << 32);
        return h1 ^ (h2 * 0x9e3779b97f4a7c15ULL);
    }
};

struct SymbolPairEqual {
    bool operator()(const std::pair<Symbol, Symbol>& a,
                    const std::pair<Symbol, Symbol>& b) const {
        return a.first == b.first && a.second == b.second;
    }
};

/// Grammar-based encoder using pattern discovery
class GrammarEncoder {
    const ByteAtomTable& atoms_;

    // All discovered rules
    std::vector<GrammarRule> rules_;

    // Pending compositions for DB
    std::vector<std::tuple<NodeRef, NodeRef, NodeRef>> pending_;

    // Minimum frequency to create a rule
    static constexpr std::uint32_t MIN_FREQ = 2;

public:
    GrammarEncoder() : atoms_(ByteAtomTable::instance()) {
        rules_.reserve(10000);
        pending_.reserve(10000);
    }

    /// Encode content using grammar-based pattern discovery
    [[nodiscard]] NodeRef encode(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};
        if (len == 1) return atoms_[data[0]];

        // Initialize sequence with byte atoms
        std::vector<Symbol> seq;
        seq.reserve(len);
        for (std::size_t i = 0; i < len; ++i) {
            seq.push_back({atoms_[data[i]], true});
        }

        // Iteratively find and replace repeated bigrams
        while (seq.size() > 1) {
            auto best = find_best_bigram(seq);
            if (best.freq < MIN_FREQ) break;

            // Create rule for this bigram
            GrammarRule rule;
            rule.left = best.left;
            rule.right = best.right;
            rule.freq = best.freq;

            NodeRef children[2] = {rule.left, rule.right};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            rule.ref = NodeRef::comp(h, l);

            rules_.push_back(rule);
            pending_.push_back({rule.ref, rule.left, rule.right});

            // Replace all occurrences
            replace_bigram(seq, best.left, best.right, rule.ref);
        }

        // Build final tree from remaining symbols
        return build_final_tree(seq);
    }

    [[nodiscard]] NodeRef encode(const std::string& s) {
        return encode(reinterpret_cast<const std::uint8_t*>(s.data()), s.size());
    }

    /// Get pending compositions
    [[nodiscard]] const auto& pending() const { return pending_; }

    /// Get discovered rules
    [[nodiscard]] const auto& rules() const { return rules_; }

    void clear() {
        rules_.clear();
        pending_.clear();
    }

private:
    struct BigramCandidate {
        NodeRef left;
        NodeRef right;
        std::uint32_t freq;
    };

    /// Find most frequent bigram in sequence
    [[nodiscard]] BigramCandidate find_best_bigram(const std::vector<Symbol>& seq) {
        std::unordered_map<std::pair<Symbol, Symbol>, std::uint32_t,
                           SymbolPairHash, SymbolPairEqual> freq_map;

        for (std::size_t i = 0; i + 1 < seq.size(); ++i) {
            auto pair = std::make_pair(seq[i], seq[i + 1]);
            freq_map[pair]++;
        }

        BigramCandidate best{{}, {}, 0};
        for (const auto& [pair, freq] : freq_map) {
            if (freq > best.freq) {
                best.left = pair.first.ref;
                best.right = pair.second.ref;
                best.freq = freq;
            }
        }

        return best;
    }

    /// Replace all occurrences of bigram with rule reference
    void replace_bigram(std::vector<Symbol>& seq,
                        NodeRef left, NodeRef right, NodeRef rule_ref) {
        std::vector<Symbol> result;
        result.reserve(seq.size());

        std::size_t i = 0;
        while (i < seq.size()) {
            if (i + 1 < seq.size() &&
                seq[i].ref.id_high == left.id_high &&
                seq[i].ref.id_low == left.id_low &&
                seq[i + 1].ref.id_high == right.id_high &&
                seq[i + 1].ref.id_low == right.id_low) {
                // Replace with rule
                result.push_back({rule_ref, false});
                i += 2;
            } else {
                result.push_back(seq[i]);
                i++;
            }
        }

        seq = std::move(result);
    }

    /// Build tree from remaining symbols
    [[nodiscard]] NodeRef build_final_tree(std::vector<Symbol>& seq) {
        if (seq.empty()) return NodeRef{};
        if (seq.size() == 1) return seq[0].ref;

        // Pairwise reduction
        while (seq.size() > 1) {
            std::vector<Symbol> next;
            next.reserve((seq.size() + 1) / 2);

            for (std::size_t i = 0; i < seq.size(); i += 2) {
                if (i + 1 < seq.size()) {
                    NodeRef children[2] = {seq[i].ref, seq[i + 1].ref};
                    auto [h, l] = MerkleHash::compute(children, children + 2);
                    NodeRef comp = NodeRef::comp(h, l);

                    pending_.push_back({comp, seq[i].ref, seq[i + 1].ref});
                    next.push_back({comp, false});
                } else {
                    next.push_back(seq[i]);
                }
            }

            seq = std::move(next);
        }

        return seq[0].ref;
    }
};

} // namespace hartonomous
