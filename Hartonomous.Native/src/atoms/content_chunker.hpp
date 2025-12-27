#pragma once

/// CONTENT-DEFINED CHUNKING
///
/// The problem: Balanced binary tree splits "Captain Ahab" differently
/// when it appears in Moby Dick vs standalone. Substring queries fail.
///
/// The solution: Content-defined chunking using rolling hash.
/// Split points are determined by content, not position.
/// Same content = same chunks = same composition roots = queryable substrings.
///
/// Uses Rabin fingerprinting with mask-based boundary detection.

#include <cstdint>
#include <vector>
#include <span>

namespace hartonomous {

/// Content-defined chunk with RLE support
struct Chunk {
    const std::uint8_t* data;
    std::size_t length;
    std::uint32_t repeat_count;  // RLE: 1 = no repeat
};

/// Rabin fingerprint rolling hash for content-defined chunking
class ContentChunker {
    // Rabin polynomial parameters (proven prime)
    static constexpr std::uint64_t PRIME = 0x3DA3358B4DC173ULL;
    static constexpr std::uint64_t MOD = (1ULL << 48) - 1;

    // Chunk size parameters
    std::size_t min_chunk_;
    std::size_t max_chunk_;
    std::uint64_t mask_;  // Determines average chunk size

public:
    /// Create chunker with target average size (must be power of 2)
    /// min = avg/4, max = avg*4
    explicit ContentChunker(std::size_t avg_chunk_size = 64)
        : min_chunk_(avg_chunk_size / 4)
        , max_chunk_(avg_chunk_size * 4)
        , mask_(avg_chunk_size - 1)  // Works when avg is power of 2
    {
        // Ensure minimum viable chunks
        if (min_chunk_ < 4) min_chunk_ = 4;
        if (max_chunk_ < 16) max_chunk_ = 256;
    }

    /// Chunk data using content-defined boundaries
    /// Returns chunks that will be consistent regardless of position in larger content
    [[nodiscard]] std::vector<Chunk> chunk(const std::uint8_t* data, std::size_t len) const {
        std::vector<Chunk> chunks;
        if (len == 0) return chunks;

        chunks.reserve(len / ((min_chunk_ + max_chunk_) / 2) + 1);

        std::size_t pos = 0;
        while (pos < len) {
            std::size_t chunk_end = find_boundary(data, len, pos);

            Chunk c;
            c.data = data + pos;
            c.length = chunk_end - pos;
            c.repeat_count = 1;

            // RLE: Check if this chunk repeats
            while (chunk_end + c.length <= len) {
                bool matches = true;
                for (std::size_t i = 0; i < c.length && matches; ++i) {
                    if (data[chunk_end + i] != c.data[i]) matches = false;
                }
                if (matches) {
                    c.repeat_count++;
                    chunk_end += c.length;
                } else {
                    break;
                }
            }

            chunks.push_back(c);
            pos = chunk_end;
        }

        return chunks;
    }

    /// Chunk string data
    [[nodiscard]] std::vector<Chunk> chunk(const char* data, std::size_t len) const {
        return chunk(reinterpret_cast<const std::uint8_t*>(data), len);
    }

    [[nodiscard]] std::vector<Chunk> chunk(const std::string& s) const {
        return chunk(s.data(), s.size());
    }

private:
    /// Find content-defined boundary starting from pos
    [[nodiscard]] std::size_t find_boundary(
        const std::uint8_t* data, std::size_t len, std::size_t pos) const
    {
        std::size_t end = pos + min_chunk_;
        if (end >= len) return len;

        std::size_t max_end = pos + max_chunk_;
        if (max_end > len) max_end = len;

        // Rolling hash
        std::uint64_t hash = 0;

        // Initialize hash with min_chunk bytes
        for (std::size_t i = pos; i < end; ++i) {
            hash = ((hash << 8) | data[i]) % MOD;
        }

        // Roll forward looking for boundary
        while (end < max_end) {
            // Boundary condition: low bits of hash match mask
            if ((hash & mask_) == 0) {
                return end;
            }

            // Roll hash forward
            hash = ((hash << 8) | data[end]) % MOD;
            ++end;
        }

        // Max chunk reached
        return end;
    }
};

/// Hierarchical chunking: chunks of chunks for large content
/// Creates natural paragraph/section boundaries
class HierarchicalChunker {
    ContentChunker level1_;  // ~64 byte chunks (words/phrases)
    ContentChunker level2_;  // ~512 byte chunks (sentences/paragraphs)
    ContentChunker level3_;  // ~4096 byte chunks (sections)

public:
    HierarchicalChunker()
        : level1_(64)
        , level2_(512)
        , level3_(4096)
    {}

    /// Get appropriate chunker for content size
    [[nodiscard]] const ContentChunker& for_size(std::size_t len) const {
        if (len < 256) return level1_;
        if (len < 2048) return level2_;
        return level3_;
    }

    /// Chunk with automatic level selection
    [[nodiscard]] std::vector<Chunk> chunk(const std::uint8_t* data, std::size_t len) const {
        return for_size(len).chunk(data, len);
    }
};

} // namespace hartonomous
