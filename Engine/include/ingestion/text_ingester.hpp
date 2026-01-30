/**
 * @file text_ingester.hpp
 * @brief Text ingestion pipeline: Text → Atoms → Compositions → Relations
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <unicode/codepoint_projection.hpp>
#include <geometry/super_fibonacci.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <string>
#include <vector>
#include <unordered_set>

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
    size_t atoms_existing = 0;
    size_t compositions_total = 0;
    size_t compositions_new = 0;
    size_t compositions_existing = 0;
    size_t relations_total = 0;
    size_t original_bytes = 0;
    size_t stored_bytes = 0;

    double compression_ratio() const {
        if (original_bytes == 0) return 0.0;
        return 1.0 - (double)stored_bytes / (double)original_bytes;
    }
};

/**
 * @brief Text ingestion engine
 *
 * Decomposes text into hierarchical Merkle DAG:
 * - Atoms: Unicode codepoints
 * - Compositions: Words (n-grams of atoms)
 * - Relations: Sentences (n-grams of compositions)
 */
class TextIngester {
public:
    /**
     * @brief Create ingester with database connection
     */
    explicit TextIngester(PostgresConnection& db);

    /**
     * @brief Ingest text
     * @param text UTF-8 encoded text
     * @return Ingestion statistics
     */
    IngestionStats ingest(const std::string& text);

    /**
     * @brief Ingest file
     */
    IngestionStats ingest_file(const std::string& path);

private:
    struct Physicality {
        BLAKE3Pipeline::Hash id;
        Vec4 centroid;
        // Trajectory not yet supported in basic ingestion
        HilbertCurve4D::HilbertIndex hilbert_index;
    };

    struct Atom {
        char32_t codepoint;
        BLAKE3Pipeline::Hash id;
        Physicality physicality;
    };

    struct SequenceItem {
        BLAKE3Pipeline::Hash id;
        uint32_t occurrences;
    };

    struct Composition {
        std::string text;
        BLAKE3Pipeline::Hash id;
        Physicality physicality;
        std::vector<SequenceItem> sequence;
    };

    struct Relation {
        BLAKE3Pipeline::Hash id;
        Physicality physicality;
        std::vector<SequenceItem> sequence;
    };

    // Decomposition
    std::vector<Atom> decompose_atoms(const std::u32string& text);
    std::vector<Composition> decompose_compositions(const std::u32string& text);
    std::vector<Relation> decompose_relations(const std::vector<Composition>& compositions);

    // Bulk Storage
    void store_batch(
        const std::vector<Atom>& atoms,
        const std::vector<Composition>& compositions,
        const std::vector<Relation>& relations,
        IngestionStats& stats
    );

    // Utilities
    std::u32string utf8_to_utf32(const std::string& utf8);
    std::vector<std::u32string> tokenize_words(const std::u32string& text);
    Vec4 compute_centroid(const std::vector<Vec4>& positions);
    BLAKE3Pipeline::Hash compute_composition_hash(const std::vector<SequenceItem>& sequence);
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);

    PostgresConnection& db_;
    std::unordered_set<std::string> seen_atom_ids_;
    std::unordered_set<std::string> seen_composition_ids_;
    std::unordered_set<std::string> seen_physicality_ids_;
};

} // namespace Hartonomous