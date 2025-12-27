#pragma once

/// CODEPOINT ATOM TABLE - The CORRECT atomic foundation
///
/// THE PROBLEM (from Gemini's analysis):
/// ByteAtomTable treats bytes (0x00-0xFF) as atoms, but semantic grounding
/// treats Unicode CODEPOINTS (0x0000-0x10FFFF) as atoms.
///
/// In UTF-8, '王' (U+738B) is [0xE7, 0x8E, 0x8B] - three bytes.
/// If CPE operates on bytes, it never sees U+738B as an atom.
/// The 4D semantic coordinates for '王' are unreachable.
///
/// THE SOLUTION:
/// CPE must operate on CODEPOINTS, not bytes.
/// 1. UTF-8 decode BEFORE CPE
/// 2. Each codepoint → SemanticDecompose → HilbertEncode → AtomId
/// 3. CPE builds tree on AtomIds (which ARE codepoints, semantically grounded)
///
/// This ensures EVERY character (ASCII, CJK, Emoji, etc.) lives in the
/// 4D semantic space with its proper Page/Type/Base/Variant coordinates.

#include "node_ref.hpp"
#include "atom_id_type.hpp"
#include "semantic_decompose.hpp"
#include "merkle_hash.hpp"
#include "../hilbert/hilbert_encoder.hpp"
#include <unordered_map>
#include <shared_mutex>
#include <mutex>
#include <cstdint>
#include <string>
#include <vector>

namespace hartonomous {

/// Codepoint Atom - a Unicode character with its semantic position
struct CodepointAtom {
    std::int32_t codepoint;      // Unicode codepoint (0x0000 - 0x10FFFF)
    SemanticCoord coord;          // 4D semantic coordinates
    NodeRef ref;                  // 128-bit Hilbert-based identifier
};

/// Codepoint Atom Table - maps codepoints to atoms with semantic positions
///
/// Unlike ByteAtomTable (256 entries), this handles the full Unicode range.
/// Uses lazy initialization + caching since 1.1M atoms is too large for precomputation.
class CodepointAtomTable {
    mutable std::shared_mutex mutex_;
    mutable std::unordered_map<std::int32_t, CodepointAtom> cache_;

public:
    CodepointAtomTable() {
        // Pre-warm cache with ASCII (most common)
        for (std::int32_t cp = 0; cp < 128; ++cp) {
            auto atom = compute_atom(cp);
            cache_[cp] = atom;
        }
    }

    /// Get atom for codepoint - lazy evaluation with caching
    [[nodiscard]] CodepointAtom get(std::int32_t codepoint) const {
        // Fast path: check cache with shared lock
        {
            std::shared_lock lock(mutex_);
            auto it = cache_.find(codepoint);
            if (it != cache_.end()) {
                return it->second;
            }
        }

        // Slow path: compute and cache with exclusive lock
        auto atom = compute_atom(codepoint);
        {
            std::unique_lock lock(mutex_);
            cache_[codepoint] = atom;
        }
        return atom;
    }

    /// Operator[] for convenience
    [[nodiscard]] CodepointAtom operator[](std::int32_t codepoint) const {
        return get(codepoint);
    }

    /// Get NodeRef for codepoint (convenience)
    [[nodiscard]] NodeRef ref(std::int32_t codepoint) const {
        return get(codepoint).ref;
    }

    /// Check if codepoint is valid Unicode
    [[nodiscard]] static bool is_valid(std::int32_t codepoint) {
        return codepoint >= 0 && codepoint <= 0x10FFFF &&
               !(codepoint >= 0xD800 && codepoint <= 0xDFFF);  // Exclude surrogates
    }

    /// Get singleton instance
    static CodepointAtomTable& instance() {
        static CodepointAtomTable table;
        return table;
    }

private:
    /// Compute atom for codepoint (semantic decomposition + Hilbert encoding)
    [[nodiscard]] static CodepointAtom compute_atom(std::int32_t codepoint) {
        CodepointAtom atom;
        atom.codepoint = codepoint;

        // Step 1: Semantic decomposition (codepoint → 4D coordinates)
        atom.coord = SemanticDecompose::get_coord(codepoint);

        // Step 2: Get AtomId via SemanticDecompose (which uses SemanticHilbert)
        // This ensures consistency with how atoms are seeded in the database
        AtomId id = SemanticDecompose::get_atom_id(codepoint);

        atom.ref = NodeRef::atom(id);
        return atom;
    }
};

// ============================================================================
// UTF-8 DECODING - Convert byte stream to codepoint stream
// ============================================================================

/// UTF-8 decoder state machine
class UTF8Decoder {
public:
    /// Decode UTF-8 bytes to codepoints
    [[nodiscard]] static std::vector<std::int32_t> decode(
        const std::uint8_t* data, std::size_t len)
    {
        std::vector<std::int32_t> codepoints;
        codepoints.reserve(len);  // Upper bound

        std::size_t i = 0;
        while (i < len) {
            std::int32_t cp;
            std::size_t bytes_read = decode_one(data + i, len - i, cp);

            if (bytes_read == 0) {
                // Invalid UTF-8: treat as raw byte (fallback to byte semantics)
                cp = static_cast<std::int32_t>(data[i]);
                bytes_read = 1;
            }

            codepoints.push_back(cp);
            i += bytes_read;
        }

        return codepoints;
    }

    /// Decode from std::string
    [[nodiscard]] static std::vector<std::int32_t> decode(const std::string& text) {
        return decode(reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
    }

    /// Decode single codepoint, return bytes consumed
    [[nodiscard]] static std::size_t decode_one(
        const std::uint8_t* data, std::size_t len, std::int32_t& out_cp)
    {
        if (len == 0) {
            out_cp = 0;
            return 0;
        }

        std::uint8_t b0 = data[0];

        // ASCII (0xxxxxxx)
        if ((b0 & 0x80) == 0) {
            out_cp = static_cast<std::int32_t>(b0);
            return 1;
        }

        // 2-byte (110xxxxx 10xxxxxx)
        if ((b0 & 0xE0) == 0xC0) {
            if (len < 2 || (data[1] & 0xC0) != 0x80) {
                return 0;  // Invalid
            }
            out_cp = ((b0 & 0x1F) << 6) | (data[1] & 0x3F);
            return 2;
        }

        // 3-byte (1110xxxx 10xxxxxx 10xxxxxx)
        if ((b0 & 0xF0) == 0xE0) {
            if (len < 3 || (data[1] & 0xC0) != 0x80 || (data[2] & 0xC0) != 0x80) {
                return 0;  // Invalid
            }
            out_cp = ((b0 & 0x0F) << 12) | ((data[1] & 0x3F) << 6) | (data[2] & 0x3F);
            return 3;
        }

        // 4-byte (11110xxx 10xxxxxx 10xxxxxx 10xxxxxx)
        if ((b0 & 0xF8) == 0xF0) {
            if (len < 4 || (data[1] & 0xC0) != 0x80 ||
                (data[2] & 0xC0) != 0x80 || (data[3] & 0xC0) != 0x80) {
                return 0;  // Invalid
            }
            out_cp = ((b0 & 0x07) << 18) | ((data[1] & 0x3F) << 12) |
                     ((data[2] & 0x3F) << 6) | (data[3] & 0x3F);
            return 4;
        }

        return 0;  // Invalid leading byte
    }

    /// Encode codepoint to UTF-8
    static std::size_t encode_one(std::int32_t cp, std::uint8_t* out) {
        if (cp < 0x80) {
            out[0] = static_cast<std::uint8_t>(cp);
            return 1;
        }
        if (cp < 0x800) {
            out[0] = static_cast<std::uint8_t>(0xC0 | (cp >> 6));
            out[1] = static_cast<std::uint8_t>(0x80 | (cp & 0x3F));
            return 2;
        }
        if (cp < 0x10000) {
            out[0] = static_cast<std::uint8_t>(0xE0 | (cp >> 12));
            out[1] = static_cast<std::uint8_t>(0x80 | ((cp >> 6) & 0x3F));
            out[2] = static_cast<std::uint8_t>(0x80 | (cp & 0x3F));
            return 3;
        }
        out[0] = static_cast<std::uint8_t>(0xF0 | (cp >> 18));
        out[1] = static_cast<std::uint8_t>(0x80 | ((cp >> 12) & 0x3F));
        out[2] = static_cast<std::uint8_t>(0x80 | ((cp >> 6) & 0x3F));
        out[3] = static_cast<std::uint8_t>(0x80 | (cp & 0x3F));
        return 4;
    }

    /// Encode codepoints to UTF-8 string
    [[nodiscard]] static std::string encode(const std::vector<std::int32_t>& codepoints) {
        std::string result;
        result.reserve(codepoints.size() * 2);  // Estimate

        std::uint8_t buf[4];
        for (std::int32_t cp : codepoints) {
            std::size_t len = encode_one(cp, buf);
            result.append(reinterpret_cast<const char*>(buf), len);
        }

        return result;
    }
};

// ============================================================================
// CODEPOINT-BASED PAIR ENCODING
// ============================================================================

/// Codepoint Pair Encoder - CPE on codepoints, not bytes
///
/// This is the CORRECT version of pair encoding that preserves semantic grounding.
/// Input: UTF-8 text → codepoints
/// Output: Composition tree where leaves are CODEPOINT atoms with full 4D semantics
class CodepointPairEncoder {
    const CodepointAtomTable& atoms_;

public:
    explicit CodepointPairEncoder(const CodepointAtomTable& atoms = CodepointAtomTable::instance())
        : atoms_(atoms)
    {}

    /// Encode text to composition tree root
    /// Properly decodes UTF-8 and builds tree on codepoints
    [[nodiscard]] NodeRef encode(const std::string& text) const {
        auto codepoints = UTF8Decoder::decode(text);
        return encode_codepoints(codepoints);
    }

    /// Encode raw bytes (assumes UTF-8)
    [[nodiscard]] NodeRef encode(const std::uint8_t* data, std::size_t len) const {
        auto codepoints = UTF8Decoder::decode(data, len);
        return encode_codepoints(codepoints);
    }

    /// Encode codepoint sequence to composition tree
    [[nodiscard]] NodeRef encode_codepoints(const std::vector<std::int32_t>& codepoints) const {
        if (codepoints.empty()) return NodeRef{};
        if (codepoints.size() == 1) return atoms_.ref(codepoints[0]);

        // Build balanced binary tree
        return build_tree(codepoints, 0, codepoints.size());
    }

private:
    /// Build balanced binary tree recursively
    [[nodiscard]] NodeRef build_tree(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end) const
    {
        std::size_t len = end - start;
        if (len == 0) return NodeRef{};
        if (len == 1) return atoms_.ref(codepoints[start]);

        if (len == 2) {
            NodeRef left = atoms_.ref(codepoints[start]);
            NodeRef right = atoms_.ref(codepoints[start + 1]);
            return compose(left, right);
        }

        std::size_t mid = start + len / 2;
        NodeRef left = build_tree(codepoints, start, mid);
        NodeRef right = build_tree(codepoints, mid, end);
        return compose(left, right);
    }

    /// Compose two nodes into one
    [[nodiscard]] NodeRef compose(NodeRef left, NodeRef right) const {
        NodeRef children[2] = {left, right};
        auto [high, low] = MerkleHash::compute(children, children + 2);
        return NodeRef::comp(high, low);
    }
};

} // namespace hartonomous
