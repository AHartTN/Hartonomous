/**
 * @file text_ingester.hpp
 * @brief Universal text ingestion pipeline
 *
 * Decomposes any text into the Hartonomous Merkle DAG:
 *   Content (root) → Relations → Compositions → Atoms → Physicality
 *
 * Features:
 * - N-gram extraction with frequency counting
 * - Co-occurrence discovery for relation building
 * - Directional tracking (A before B vs B before A)
 * - Content as root node with full provenance
 * - RelationEvidence linking relations to source content
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <unicode/codepoint_projection.hpp>
#include <geometry/super_fibonacci.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <ingestion/ngram_extractor.hpp>
#include <string>
#include <vector>
#include <unordered_set>
#include <unordered_map>

namespace Hartonomous {

using Vec4 = Eigen::Vector4d;
using hartonomous::unicode::CodepointProjection;
using hartonomous::spatial::HilbertCurve4D;

/**
 * @brief Ingestion statistics
 */
struct IngestionStats {
    size_t atoms_total = 0;
    size_t atoms_new = 0;
    size_t compositions_total = 0;
    size_t compositions_new = 0;
    size_t relations_total = 0;
    size_t relations_new = 0;
    size_t evidence_count = 0;
    size_t original_bytes = 0;

    // N-gram stats
    size_t ngrams_extracted = 0;
    size_t ngrams_significant = 0;
    size_t cooccurrences_found = 0;
    size_t cooccurrences_significant = 0;
};

/**
 * @brief Ingestion configuration
 */
struct IngestionConfig {
    // N-gram settings
    uint32_t min_ngram_size = 1;
    uint32_t max_ngram_size = 8;
    uint32_t min_frequency = 2;       // Min occurrences to be significant
    uint32_t cooccurrence_window = 5; // Window for co-occurrence detection
    uint32_t min_cooccurrence = 2;    // Min co-occurrences for relation

    // Content metadata (required)
    BLAKE3Pipeline::Hash tenant_id;
    BLAKE3Pipeline::Hash user_id;
    uint16_t content_type = 1;        // 1 = text/plain (default)
    std::string mime_type = "text/plain";
    std::string language = "en";
    std::string source;
    std::string encoding = "utf-8";
};

/**
 * @brief Text ingestion engine
 *
 * Universal entry point for text decomposition into Hartonomous semantic graph.
 */
class TextIngester {
public:
    /**
     * @brief Create ingester with database connection
     */
    explicit TextIngester(PostgresConnection& db, const IngestionConfig& config = IngestionConfig());

    /**
     * @brief Ingest text and create full Merkle DAG
     * @param text UTF-8 encoded text
     * @return Ingestion statistics
     */
    IngestionStats ingest(const std::string& text);

    /**
     * @brief Ingest file
     */
    IngestionStats ingest_file(const std::string& path);

    /**
     * @brief Update configuration
     */
    void set_config(const IngestionConfig& config) { config_ = config; }

private:
    // Internal structures
    struct Physicality {
        BLAKE3Pipeline::Hash id;
        Vec4 centroid;
        HilbertCurve4D::HilbertIndex hilbert_index;
    };

    struct Atom {
        char32_t codepoint;
        BLAKE3Pipeline::Hash id;
        Physicality physicality;
    };

    struct SequenceItem {
        BLAKE3Pipeline::Hash id;
        uint32_t ordinal;
        uint32_t occurrences;
    };

    struct Composition {
        std::u32string text;
        BLAKE3Pipeline::Hash id;
        Physicality physicality;
        std::vector<SequenceItem> sequence;  // Atom sequence
    };

    struct Relation {
        BLAKE3Pipeline::Hash id;
        Physicality physicality;
        std::vector<SequenceItem> sequence;  // Composition sequence
        double initial_elo;
        bool is_forward;  // Direction: A typically before B
    };

    // Processing phases
    std::u32string utf8_to_utf32(const std::string& utf8);
    BLAKE3Pipeline::Hash create_content_record(const std::string& text);
    std::vector<Atom> extract_atoms(const std::u32string& text);
    std::vector<Composition> extract_compositions(const std::u32string& text,
                                                   const std::unordered_map<std::string, Atom>& atom_map);
    std::vector<Relation> extract_relations(const std::unordered_map<std::string, Composition>& comp_map);

    // Physicality computation
    Vec4 compute_centroid(const std::vector<Vec4>& positions);
    Physicality compute_physicality(const Vec4& centroid);

    // Storage
    void store_all(
        const BLAKE3Pipeline::Hash& content_id,
        const std::vector<Atom>& atoms,
        const std::vector<Composition>& compositions,
        const std::vector<Relation>& relations,
        IngestionStats& stats
    );

    // Utilities
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
    std::string hash_to_hex(const BLAKE3Pipeline::Hash& hash);
    BLAKE3Pipeline::Hash compute_sequence_hash(const std::vector<SequenceItem>& sequence);

    PostgresConnection& db_;
    IngestionConfig config_;
    NGramExtractor extractor_;

    // Deduplication caches
    std::unordered_set<std::string> seen_physicality_ids_;
    std::unordered_set<std::string> seen_atom_ids_;
    std::unordered_set<std::string> seen_composition_ids_;
    std::unordered_set<std::string> seen_relation_ids_;
};

}
