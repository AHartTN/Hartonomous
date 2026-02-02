/**
 * @file text_ingester.hpp
 * @brief Universal text ingestion pipeline
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

struct IngestionStats {
    size_t atoms_total = 0;
    size_t atoms_new = 0;
    size_t compositions_total = 0;
    size_t compositions_new = 0;
    size_t relations_total = 0;
    size_t relations_new = 0;
    size_t evidence_count = 0;
    size_t original_bytes = 0;
    size_t ngrams_extracted = 0;
    size_t ngrams_significant = 0;
    size_t cooccurrences_found = 0;
    size_t cooccurrences_significant = 0;
};

struct IngestionConfig {
    uint32_t min_ngram_size = 1;
    uint32_t max_ngram_size = 8;
    uint32_t min_frequency = 2;
    uint32_t cooccurrence_window = 5;
    uint32_t min_cooccurrence = 2;
    BLAKE3Pipeline::Hash tenant_id;
    BLAKE3Pipeline::Hash user_id;
    uint16_t content_type = 1;
    std::string mime_type = "text/plain";
    std::string language = "en";
    std::string source;
    std::string encoding = "utf-8";
};

class TextIngester {
public:
    explicit TextIngester(PostgresConnection& db, const IngestionConfig& config = IngestionConfig());
    IngestionStats ingest(const std::string& text);
    IngestionStats ingest_file(const std::string& path);
    void set_config(const IngestionConfig& config) { config_ = config; }
    void load_global_caches();

private:
    struct Physicality { BLAKE3Pipeline::Hash id; Vec4 centroid; HilbertCurve4D::HilbertIndex hilbert_index; };
    struct Atom { char32_t codepoint; BLAKE3Pipeline::Hash id; Physicality physicality; };
    struct SequenceItem { BLAKE3Pipeline::Hash id; uint32_t ordinal; uint32_t occurrences; };
    struct Composition { std::u32string text; BLAKE3Pipeline::Hash id; Physicality physicality; std::vector<SequenceItem> sequence; };
    struct Relation { BLAKE3Pipeline::Hash id; Physicality physicality; std::vector<SequenceItem> sequence; double initial_elo; bool is_forward; };

    std::u32string utf8_to_utf32(const std::string& s);
    std::string utf32_to_utf8(const std::u32string& s);
    BLAKE3Pipeline::Hash create_content_record(const std::string& text);
    std::vector<Atom> extract_atoms(const std::u32string& text);
    std::vector<Composition> extract_compositions(const std::u32string& text, const std::unordered_map<BLAKE3Pipeline::Hash, Atom, HashHasher>& atom_map);
    std::vector<Relation> extract_relations(const std::unordered_map<BLAKE3Pipeline::Hash, Composition, HashHasher>& comp_map);
    Vec4 compute_centroid(const std::vector<Vec4>& positions);
    Physicality compute_physicality(const Vec4& centroid);
    void store_all(const BLAKE3Pipeline::Hash& content_id, const std::vector<Atom>& atoms, const std::vector<Composition>& compositions, const std::vector<Relation>& relations, IngestionStats& stats);
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
    std::string hash_to_hex(const BLAKE3Pipeline::Hash& hash);
    BLAKE3Pipeline::Hash compute_sequence_hash(const std::vector<SequenceItem>& sequence, uint8_t type_tag);

    PostgresConnection& db_;
    IngestionConfig config_;
    NGramExtractor extractor_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_physicality_ids_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_atom_ids_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_composition_ids_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_relation_ids_;
};

}
