/**
 * @file text_ingester.hpp
 * @brief Universal text ingestion pipeline
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <storage/atom_lookup.hpp>
#include <ingestion/ngram_extractor.hpp>
#include <ingestion/substrate_service.hpp>
#include <utils/unicode.hpp>
#include <string>
#include <vector>
#include <unordered_set>
#include <unordered_map>

namespace Hartonomous {

struct IngestionStats {
    size_t atoms_total = 0;
    size_t atoms_new = 0;
    size_t compositions_total = 0;
    size_t compositions_new = 0;
    size_t relations_total = 0;
    size_t relations_new = 0;
    size_t evidence_count = 0;
    size_t original_bytes = 0;
    size_t stored_bytes = 0;
    double compression_ratio = 0.0;
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

    void preload_atoms();

private:
    BLAKE3Pipeline::Hash create_content_record(const std::string& text, BLAKE3Pipeline::Hash* content_hash);

    PostgresConnection& db_;
    IngestionConfig config_;
    NGramExtractor extractor_;
    AtomLookup atom_lookup_;  
    bool atoms_preloaded_ = false;
};

} // namespace Hartonomous