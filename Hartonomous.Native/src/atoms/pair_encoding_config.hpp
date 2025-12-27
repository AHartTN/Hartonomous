#pragma once

#include <cstdint>

namespace hartonomous {

/// Configuration for the pair encoding compression engine.
///
/// These defaults are tuned for general-purpose text compression with good
/// balance between compression ratio, memory usage, and processing speed.
struct PairEncodingConfig {
    /// Size of parallel processing chunks in bytes.
    /// Larger chunks = better compression (more context), but more memory.
    /// Default: 1MB provides good balance for most workloads.
    std::size_t chunk_size;

    /// Minimum frequency required before a pair can be merged.
    /// - 1: Aggressive merging, highest compression, slower
    /// - 2: Standard - only merge pairs that appear at least twice (default)
    /// - Higher: More conservative, preserves more original structure
    std::size_t min_pair_frequency;

    /// Maximum vocabulary entries (composition nodes).
    /// Limits memory usage: each entry ~24 bytes.
    /// Default: 1M entries = ~24MB vocabulary overhead.
    std::size_t max_vocabulary_size;

    /// Number of pairs to merge per iteration.
    /// Higher = fewer iterations but may miss optimal merges.
    /// Lower = more iterations but better compression quality.
    /// Default: 1000 balances quality vs speed.
    std::size_t batch_merge_count;

    /// Thread count for parallel processing. 0 = auto-detect from hardware.
    std::size_t thread_count;

    PairEncodingConfig() :
        chunk_size(1 << 18),          // 256KB: smaller chunks = more parallelism
        min_pair_frequency(3),         // Only merge pairs appearing 3+ times (faster)
        max_vocabulary_size(1 << 18),  // 256K entries: limit vocabulary growth
        batch_merge_count(2000),       // Merge top 2000 pairs per iteration (faster)
        thread_count(0) {}             // Auto-detect thread count
};

} // namespace hartonomous
