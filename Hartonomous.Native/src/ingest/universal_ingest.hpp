#pragma once

/// UNIVERSAL CONTENT INGESTION - Everything is just bytes
///
/// THE PHILOSOPHY:
/// The substrate doesn't care about modality. Text, images, audio, model weights,
/// JSON, binary - it's ALL just bytes. The substrate sees:
///
///   bytes → CPE → NodeRef → store
///
/// That's it. No special cases. No "this is an AI model so treat it differently."
/// The semantic structure emerges from the COMPOSITION TREE, not from modality-aware code.
///
/// THE TRAJECTORY INTERSECTION MODEL:
/// A concept isn't a point or a cluster - it's a TRAJECTORY through 4D semantic space.
/// "king" traces a path. "monarch" traces a path. "ruler" traces a path.
/// Where these trajectories INTERSECT - that's where meaning emerges.
///
/// NOT clustering. INTERSECTION. This is geometry:
///   - ST_Intersects(king_trajectory, monarch_trajectory) → they share meaning
///   - ST_Distance(king_trajectory, tyrant_trajectory) → semantic proximity
///   - ST_FrechetDistance for trajectory-to-trajectory similarity
///
/// The concept of "king" is the INTERSECTION of all trajectories that pass through
/// that region of semantic space. Voronoi cells are defined by trajectory crossings.
///
/// WHAT ABOUT HIGH-DIMENSIONAL DATA?
/// Embeddings are 4096-dimensional. We map them as TRAJECTORIES through 4D space.
/// Each embedding dimension = a point on the trajectory.
/// High-dimensional similarity = trajectory similarity (Frechet distance).
/// GiST index on geometry gives O(log n) spatial queries.
///
/// WHAT ABOUT RELATIONSHIPS?
/// Some content has EXPLICIT relationships:
/// - Model attention patterns: token A attends to token B
/// - Graph data: node A connects to node B
/// - Co-occurrence: word A appears with word B in document
///
/// These are stored as edges. They're DISCOVERED from content structure, not pre-computed.
/// Similarity? That's a QUERY via trajectory intersection, not a stored relationship.

#include "../db/query_store.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/codepoint_atom_table.hpp"
#include "../threading/threading.hpp"
#include <span>
#include <vector>
#include <string>
#include <string_view>
#include <filesystem>
#include <fstream>
#include <cstdint>
#include <iostream>

namespace hartonomous::ingest {

using db::QueryStore;
using db::RelType;

/// Ingestion statistics
struct IngestStats {
    std::size_t bytes_processed;
    std::size_t compositions_created;
    std::size_t trajectories_stored;
    std::size_t relationships_stored;
    std::chrono::milliseconds duration;
};

/// Universal Content Ingester - modality-agnostic
///
/// Everything goes through the same pipeline:
///   content → bytes → UTF-8 decode → CPE → NodeRef → store
///
/// High-dimensional vectors? Store as trajectories.
/// Explicit relationships? Store as edges.
/// Similarity? Query at runtime, not pre-computed.
class UniversalIngester {
    QueryStore& store_;

public:
    explicit UniversalIngester(QueryStore& store)
        : store_(store)
    {}

    // ========================================================================
    // CORE INGESTION - Everything is bytes
    // ========================================================================

    /// Ingest raw bytes as content.
    /// This is THE fundamental operation. Everything else calls this.
    [[nodiscard]] NodeRef ingest(std::span<const std::uint8_t> bytes) {
        if (bytes.empty()) return NodeRef{};

        // Decode UTF-8 to codepoints (handles invalid UTF-8 gracefully)
        auto codepoints = UTF8Decoder::decode(bytes.data(), bytes.size());

        // CPE on codepoints → NodeRef
        CodepointPairEncoder encoder;
        NodeRef ref = encoder.encode_codepoints(codepoints);

        // Store the composition tree
        store_.build_and_collect(bytes.data(), bytes.size());

        return ref;
    }

    /// Ingest string content
    [[nodiscard]] NodeRef ingest(std::string_view text) {
        return ingest(std::span<const std::uint8_t>(
            reinterpret_cast<const std::uint8_t*>(text.data()),
            text.size()));
    }

    /// Ingest file content
    [[nodiscard]] NodeRef ingest_file(const std::filesystem::path& path) {
        std::ifstream file(path, std::ios::binary | std::ios::ate);
        if (!file) return NodeRef{};

        auto size = file.tellg();
        file.seekg(0, std::ios::beg);

        std::vector<std::uint8_t> buffer(static_cast<std::size_t>(size));
        file.read(reinterpret_cast<char*>(buffer.data()), size);

        return ingest(buffer);
    }

    /// Ingest directory recursively
    IngestStats ingest_directory(const std::filesystem::path& dir) {
        auto start = std::chrono::high_resolution_clock::now();
        IngestStats stats{};

        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;

            auto size = entry.file_size();
            ingest_file(entry.path());
            stats.bytes_processed += size;
            stats.compositions_created++;
        }

        store_.flush_pending();

        auto end = std::chrono::high_resolution_clock::now();
        stats.duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

        return stats;
    }

    // ========================================================================
    // TRAJECTORY STORAGE - For high-dimensional data
    // ========================================================================

    /// Store high-dimensional vector as trajectory through 4D space.
    ///
    /// Why trajectories? A 4096-dim embedding doesn't fit in 4D space.
    /// But a SEQUENCE through 4D space does. Each dimension becomes a point.
    /// Similarity = trajectory similarity = Frechet distance.
    /// GiST index gives O(log n) queries, not O(n²) pairwise at ingestion.
    ///
    /// @param ref The NodeRef this trajectory belongs to (e.g., token ref)
    /// @param vector The high-dimensional vector (e.g., embedding)
    /// @param context Model/source context
    void store_trajectory(
        NodeRef ref,
        std::span<const float> vector,
        NodeRef context)
    {
        // Store as single trajectory
        std::vector<NodeRef> refs = {ref};
        store_.store_embedding_trajectories(
            vector.data(),
            1,  // One vector
            vector.size(),  // Dimensions
            refs,
            context,
            RelType::EMBEDDING_TRAJECTORY);
    }

    /// Bulk store trajectories (for batch ingestion)
    /// O(n) - each vector stored once
    void store_trajectories(
        std::span<const float> vectors,  // [count × dims] contiguous
        std::size_t count,
        std::size_t dims,
        std::span<const NodeRef> refs,
        NodeRef context)
    {
        store_.store_embedding_trajectories(
            vectors.data(),
            count,
            dims,
            std::vector<NodeRef>(refs.begin(), refs.end()),
            context,
            RelType::EMBEDDING_TRAJECTORY);
    }

    // ========================================================================
    // RELATIONSHIP STORAGE - For explicit edges
    // ========================================================================

    /// Store explicit relationship discovered from content.
    ///
    /// NOT similarity. Similarity is computed at query time.
    /// These are STRUCTURAL relationships:
    /// - Attention: "token A attends to token B with weight W"
    /// - Co-occurrence: "word A appears near word B"
    /// - Graph edge: "node A connects to node B"
    void store_relationship(
        NodeRef from,
        NodeRef to,
        double weight,
        RelType type = RelType::SEMANTIC_LINK)
    {
        std::vector<std::tuple<NodeRef, NodeRef, double>> rels = {
            {from, to, weight}
        };
        store_.store_model_weights(rels, NodeRef{}, type);
    }

    /// Bulk store relationships
    void store_relationships(
        std::span<const std::tuple<NodeRef, NodeRef, double>> rels,
        NodeRef context,
        RelType type = RelType::SEMANTIC_LINK)
    {
        store_.store_model_weights(
            std::vector<std::tuple<NodeRef, NodeRef, double>>(rels.begin(), rels.end()),
            context,
            type);
    }

    // ========================================================================
    // VOCABULARY INGESTION - Tokens are just strings
    // ========================================================================

    /// Ingest vocabulary - each token is just a string.
    /// "king" → CPE → NodeRef. Same "king" = same NodeRef. Always.
    /// Multiple models, same tokens, same NodeRefs. They CONVERGE.
    ///
    /// Returns vector of NodeRefs, one per token.
    [[nodiscard]] std::vector<NodeRef> ingest_vocabulary(
        std::span<const std::string> tokens)
    {
        std::vector<NodeRef> refs;
        refs.reserve(tokens.size());

        for (const auto& token : tokens) {
            // Token → CPE → NodeRef
            // Deterministic: same token = same NodeRef, always
            refs.push_back(ingest(token));
        }

        store_.flush_pending();
        return refs;
    }

    // ========================================================================
    // MODEL INGESTION - Unified, modality-agnostic
    // ========================================================================

    /// Ingest AI model package.
    ///
    /// NOT special. A model is:
    /// 1. Vocabulary (strings → NodeRefs via CPE)
    /// 2. Embeddings (high-dim vectors → trajectories)
    /// 3. Other weights (stored sparsely if significant)
    ///
    /// NO O(n²). NO modality-specific code. Just:
    /// - Content → ingest()
    /// - Vectors → store_trajectory()
    /// - Relationships → store_relationship()
    IngestStats ingest_model_package(const std::filesystem::path& dir);

    // ========================================================================
    // TRAJECTORY INTERSECTION QUERIES - Where meaning emerges
    // ========================================================================

    /// Find trajectories that INTERSECT with the given NodeRef's trajectory.
    /// THIS is where meaning lives - trajectory intersections in 4D space.
    /// Uses spatial index, O(log n).
    ///
    /// Returns NodeRefs whose trajectories intersect or pass near this one.
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> find_intersecting(
        NodeRef ref,
        double distance_threshold = 0.1)
    {
        // Query trajectories within distance threshold
        return store_.query_trajectory_intersections(ref, distance_threshold);
    }

    /// Find trajectories by Frechet distance (trajectory similarity)
    /// Lower Frechet distance = more similar paths through semantic space
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> find_similar_trajectories(
        NodeRef ref,
        std::size_t limit = 10)
    {
        return store_.query_trajectory_neighbors(ref, limit);
    }

    /// Find compositions whose 4D positions fall within a bounding box.
    /// This is a raw spatial query - useful for exploring regions.
    [[nodiscard]] std::vector<NodeRef> find_in_region(
        double page_min, double page_max,
        double type_min, double type_max,
        double base_min, double base_max,
        double variant_min, double variant_max,
        std::size_t limit = 100)
    {
        return store_.query_bounding_box(
            page_min, page_max, type_min, type_max,
            base_min, base_max, variant_min, variant_max, limit);
    }

    // ========================================================================
    // FLUSH
    // ========================================================================

    /// Flush pending compositions to database
    void flush() {
        store_.flush_pending();
    }
};

// ============================================================================
// INLINE IMPLEMENTATIONS
// ============================================================================

inline IngestStats UniversalIngester::ingest_model_package(
    const std::filesystem::path& dir)
{
    auto start = std::chrono::high_resolution_clock::now();
    IngestStats stats{};

    std::cerr << "ingest_model_package: " << dir << "\n";

    // First pass: ingest all text/config content
    for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
        if (!entry.is_regular_file()) continue;

        auto ext = entry.path().extension().string();

        // Text-based files → full semantic ingestion
        if (ext == ".json" || ext == ".txt" || ext == ".yaml" ||
            ext == ".yml" || ext == ".md" || ext == ".py" ||
            ext == ".toml" || ext == ".ini" || ext == ".cfg") {

            auto ref = ingest_file(entry.path());
            stats.bytes_processed += entry.file_size();
            stats.compositions_created++;

            std::cerr << "  ingested: " << entry.path().filename() << "\n";
        }
    }

    flush();

    // Second pass: safetensors (handled by model_ingest.hpp)
    // This is not modality-specific - it's just reading structured binary
    // and converting it to universal representations (trajectories, edges)

    auto end = std::chrono::high_resolution_clock::now();
    stats.duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

    return stats;
}

} // namespace hartonomous::ingest
