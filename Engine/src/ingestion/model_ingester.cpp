/**
 * @file model_ingester.cpp
 * @brief AI model ingestion: Extract semantic edges from models
 *
 * Architecture insight:
 * - "King" in substrate = composition of atoms [K,i,n,g] = just the word
 * - "King" in AI model = entire CONCEPT with all learned relationships
 *
 * Model ingestion EXTRACTS the concept by mining all semantic edges:
 * - Embedding KNN → semantic neighbor relations
 * - Embedding analogies → structural relations (king:queen :: man:woman)
 * - Attention patterns → contextual relations
 * - All edges become Relations with Evidence from the model
 *
 * We do NOT project embeddings to S³. Physicality is intrinsic to content.
 * We MINE embeddings for Relations.
 */

#include <ingestion/model_ingester.hpp>
#include <storage/physicality_store.hpp>
#include <storage/atom_store.hpp>
#include <storage/atom_lookup.hpp>
#include <storage/composition_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <storage/content_store.hpp>
#include <ml/model_extraction.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <iostream>
#include <iomanip>
#include <sstream>
#include <cmath>
#include <algorithm>
#include <atomic>
#include <omp.h>

namespace Hartonomous {

using namespace hartonomous::spatial;
using namespace hartonomous::ml;

// Structure to hold records collected during parallel extraction
struct ThreadLocalRecords {
    std::vector<PhysicalityRecord> phys;
    std::vector<RelationRecord> rel;
    std::vector<RelationSequenceRecord> rel_seq;
    std::vector<RelationRatingRecord> rating;
    std::vector<RelationEvidenceRecord> ev;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> rel_seen;  // Dedup relations within thread
    size_t relations_created = 0;

    void clear() {
        phys.clear();
        rel.clear();
        rel_seq.clear();
        rating.clear();
        ev.clear();
        phys_seen.clear();
        rel_seen.clear();
    }
};

ModelIngester::ModelIngester(PostgresConnection& db, const ModelIngestionConfig& config)
    : db_(db), config_(config) {
    // Model ID = hash of model identifier
    std::vector<uint8_t> id_data;
    id_data.push_back(0x4D); // 'M' for Model
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    model_id_ = BLAKE3Pipeline::hash(id_data);
}

ModelIngestionStats ModelIngester::ingest_package(const std::filesystem::path& package_dir) {
    ModelIngestionStats stats;
    // Removed global cache loading - relying on DB ON CONFLICT

    try {
        SafetensorLoader loader(package_dir.string());
        auto& metadata = loader.metadata();
        stats.vocab_tokens = metadata.vocab.size();
        std::cout << "Ingesting model with " << stats.vocab_tokens << " vocab tokens..." << std::endl;

        // 0. Store Content record for the model (needed for evidence FK)
        std::cout << "  Creating content record for model: " << hash_to_uuid(model_id_) << std::endl;
        try {
            // Small transaction for content record
            PostgresConnection::Transaction txn(db_);
            ContentStore content_store(db_);
            content_store.store({
                model_id_,
                config_.tenant_id,
                config_.user_id,
                2, // Content Type: Model
                model_id_,
                0, // Size unknown/irrelevant
                "application/octet-stream", // Mime
                "en", // Language
                package_dir.string(), // Source
                "binary" // Encoding
            });
            content_store.flush();
            txn.commit();
            std::cout << "  Content record stored." << std::endl;
        } catch (const std::exception& e) {
            std::cerr << "  Error creating content record: " << e.what() << std::endl;
            throw;
        }

        // 1. Ingest vocab tokens as compositions (SAME PIPELINE AS TEXT)
        auto token_to_comp = ingest_vocab_as_text(metadata.vocab, stats);

        // Global session cache for relation deduplication across phases
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> session_rel_seen;

        // 2. Extract semantic edges from embeddings
        auto embeddings = loader.get_embeddings();
        if (embeddings.rows() > 0) {
            std::cout << "  Extracting semantic edges from embeddings ("
                      << embeddings.rows() << "x" << embeddings.cols() << ")..." << std::endl;
            extract_embedding_edges(metadata.vocab, embeddings, token_to_comp, session_rel_seen, stats);
        }

        // 3. Extract attention layer edges (requires embeddings for projection)
        auto attention_layers = loader.get_attention_layers();
        if (!attention_layers.empty() && embeddings.rows() > 0) {
            std::cout << "  Extracting edges from " << attention_layers.size() << " attention layers..." << std::endl;
            extract_attention_layer_edges(attention_layers, metadata.vocab, embeddings, token_to_comp, session_rel_seen, stats);
        }

        // 4. Extract FFN layer edges (requires embeddings for activation)
        auto ffn_layers = loader.get_ffn_layers();
        if (!ffn_layers.empty() && embeddings.rows() > 0) {
            std::cout << "  Extracting edges from " << ffn_layers.size() << " FFN layers..." << std::endl;
            extract_ffn_layer_edges(ffn_layers, metadata.vocab, embeddings, token_to_comp, session_rel_seen, stats);
        }

        std::cout << "  Model ingestion complete." << std::endl;

    } catch (const std::exception& e) {
        std::cerr << "Model ingestion failed: " << e.what() << std::endl;
    }

    return stats;
}

std::unordered_map<std::string, BLAKE3Pipeline::Hash>
ModelIngester::ingest_vocab_as_text(const std::vector<std::string>& vocab, ModelIngestionStats& stats) {
    std::cout << "  Ingesting vocab tokens as compositions..." << std::endl;
    std::unordered_map<std::string, BLAKE3Pipeline::Hash> token_to_comp;

    // 1. Load atoms for all unique codepoints in vocab (Batched, efficient)
    AtomLookup atom_lookup(db_);
    std::unordered_set<uint32_t> unique_cps;
    for (const auto& token : vocab) {
        for (size_t i = 0; i < token.size(); ) {
            uint8_t c = token[i];
            char32_t cp = 0; size_t len = 1;
            if (c < 0x80) { cp = c; }
            else if ((c >> 5) == 0x6) { cp = c & 0x1F; len = 2; }
            else if ((c >> 4) == 0xE) { cp = c & 0x0F; len = 3; }
            else if ((c >> 3) == 0x1E) { cp = c & 0x07; len = 4; }
            for (size_t j = 1; j < len && i + j < token.size(); ++j) {
                uint8_t cc = token[i + j];
                if ((cc >> 6) == 0x2) cp = (cp << 6) | (cc & 0x3F);
            }
            unique_cps.insert(cp); i += len;
        }
    }
    auto atom_map = atom_lookup.lookup_batch({unique_cps.begin(), unique_cps.end()});

    // 2. Parallel record generation
    int num_threads = omp_get_max_threads();
    struct LocalVocabRecords {
        std::vector<PhysicalityRecord> phys;
        std::vector<CompositionRecord> comp;
        std::vector<CompositionSequenceRecord> seq;
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;
        std::vector<std::pair<std::string, BLAKE3Pipeline::Hash>> token_mappings;
        size_t created = 0;
    };
    std::vector<LocalVocabRecords> thread_records(num_threads);

    #pragma omp parallel for schedule(dynamic, 256)
    for (size_t i = 0; i < vocab.size(); ++i) {
        const auto& token = vocab[i];
        int tid = omp_get_thread_num();
        auto& tl = thread_records[tid];

        std::vector<BLAKE3Pipeline::Hash> atom_ids;
        std::vector<Eigen::Vector4d> positions;

        // Decode UTF-8
        for (size_t k = 0; k < token.size(); ) {
            uint8_t c = token[k]; char32_t cp = 0; size_t len = 1;
            if (c < 0x80) { cp = c; }
            else if ((c >> 5) == 0x6) { cp = c & 0x1F; len = 2; }
            else if ((c >> 4) == 0xE) { cp = c & 0x0F; len = 3; }
            else if ((c >> 3) == 0x1E) { cp = c & 0x07; len = 4; }
            for (size_t j = 1; j < len && k + j < token.size(); ++j) {
                uint8_t cc = token[k + j];
                if ((cc >> 6) == 0x2) cp = (cp << 6) | (cc & 0x3F);
            }
            auto it = atom_map.find(cp);
            if (it != atom_map.end()) {
                atom_ids.push_back(it->second.id);
                positions.push_back(it->second.position);
            }
            k += len;
        }

        if (atom_ids.empty()) continue;

        std::vector<uint8_t> comp_data = {0x43};
        for (const auto& aid : atom_ids) comp_data.insert(comp_data.end(), aid.begin(), aid.end());
        auto comp_id = BLAKE3Pipeline::hash(comp_data);
        tl.token_mappings.push_back({token, comp_id});

        // We skip seen_composition_ids_ check here for parallel speed, 
        // relying on ON CONFLICT DO NOTHING in Store.
        
        Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
        for (const auto& p : positions) centroid += p;
        centroid /= static_cast<double>(positions.size());
        double norm = centroid.norm();
        if (norm > 1e-10) centroid /= norm; else centroid = Eigen::Vector4d(1, 0, 0, 0);

        std::vector<uint8_t> phys_data = {0x50};
        phys_data.insert(phys_data.end(), reinterpret_cast<const uint8_t*>(centroid.data()), reinterpret_cast<const uint8_t*>(centroid.data()) + 32);
        auto phys_id = BLAKE3Pipeline::hash(phys_data);

        if (tl.phys_seen.insert(phys_id).second) {
            Eigen::Vector4d hc; for (int k = 0; k < 4; ++k) hc[k] = (centroid[k] + 1.0) / 2.0;
            tl.phys.push_back({phys_id, HilbertCurve4D::encode(hc), centroid, ""});
        }

        tl.comp.push_back({comp_id, phys_id});
        tl.created++;

        for (size_t k = 0; k < atom_ids.size(); ) {
            uint32_t ordinal = static_cast<uint32_t>(k);
            uint32_t occurrences = 1;
            while (k + occurrences < atom_ids.size() && atom_ids[k + occurrences] == atom_ids[k]) ++occurrences;
            std::vector<uint8_t> seq_data = {0x53};
            seq_data.insert(seq_data.end(), comp_id.begin(), comp_id.end());
            seq_data.insert(seq_data.end(), atom_ids[k].begin(), atom_ids[k].end());
            seq_data.insert(seq_data.end(), reinterpret_cast<uint8_t*>(&ordinal), reinterpret_cast<uint8_t*>(&ordinal) + 4);
            tl.seq.push_back({BLAKE3Pipeline::hash(seq_data), comp_id, atom_ids[k], ordinal, occurrences});
            k += occurrences;
        }
    }

    // 3. Merge and Flush - create stores ONCE with direct mode
    std::vector<PhysicalityRecord> phys_records;
    std::vector<CompositionRecord> comp_records;
    std::vector<CompositionSequenceRecord> seq_records;

    // Use temp tables (true) for ALL stores in vocab ingestion to handle duplicates safely
    // This prevents "duplicate key value" errors if tokens already exist (e.g. from previous runs or text ingestion)
    PhysicalityStore phys_store(db_, true);         // Needs temp table for dedup
    CompositionStore comp_store(db_, true);         // Needs temp table for ON CONFLICT DO NOTHING
    CompositionSequenceStore seq_store(db_, true);  // Needs temp table for safety

    size_t total_flushed = 0;
    auto flush_batch = [&]() {
        if (comp_records.empty()) return;
        PostgresConnection::Transaction txn(db_);
        for (auto& r : phys_records) phys_store.store(r);
        phys_store.flush();
        for (auto& r : comp_records) comp_store.store(r);
        comp_store.flush();
        for (auto& r : seq_records) seq_store.store(r);
        seq_store.flush();
        txn.commit();
        total_flushed += comp_records.size();
        phys_records.clear(); comp_records.clear(); seq_records.clear();
    };

    const size_t BATCH_SIZE = config_.db_batch_size;
    for (auto& tl : thread_records) {
        for (auto& m : tl.token_mappings) token_to_comp[m.first] = m.second;
        phys_records.insert(phys_records.end(), tl.phys.begin(), tl.phys.end());
        comp_records.insert(comp_records.end(), tl.comp.begin(), tl.comp.end());
        seq_records.insert(seq_records.end(), tl.seq.begin(), tl.seq.end());
        stats.compositions_created += tl.created;

        if (comp_records.size() >= BATCH_SIZE) {
            std::cout << "\r    Vocab: " << total_flushed << " compositions written..." << std::flush;
            flush_batch();
        }
    }
    flush_batch();
    std::cout << "\r    Vocab: " << total_flushed << " compositions written. Done." << std::endl;

    return token_to_comp;
}

void ModelIngester::extract_embedding_edges(
    const std::vector<std::string>& vocab,
    const Eigen::MatrixXf& embeddings,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2) return;

    // Build HNSW index for fast KNN
    std::cout << "    Building HNSW index (" << n << " vectors, " << embeddings.cols() << "D)..." << std::flush;
    hnswlib::L2Space space(embeddings.cols());
    hnswlib::HierarchicalNSW<float> index(&space, n, 16, 200);
    
    // Parallel index construction
    #pragma omp parallel for schedule(dynamic, 512)
    for (size_t i = 0; i < n; ++i) {
        index.addPoint(embeddings.row(i).data(), i);
    }
    std::cout << "\r    HNSW index built (" << n << " vectors).                    " << std::endl;

    // Set ef for search - lower = faster but less accurate recall
    index.setEf(config_.hnsw_ef_search);

    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);
    std::cout << "    Extracting KNN edges (k=" << k_neighbors
              << ", threshold>=" << config_.embedding_similarity_threshold
              << ", ef=" << config_.hnsw_ef_search << ")..." << std::endl;

    int num_threads = omp_get_max_threads();
    std::vector<ThreadLocalRecords> thread_records(num_threads);
    std::atomic<size_t> progress_counter{0};

    #pragma omp parallel for schedule(dynamic, 128)
    for (size_t i = 0; i < n; ++i) {
        int tid = omp_get_thread_num();
        auto& tl = thread_records[tid];

        if (token_to_comp.find(vocab[i]) == token_to_comp.end()) continue;
        auto src_comp_id = token_to_comp.at(vocab[i]);

        auto neighbors = index.searchKnn(embeddings.row(i).data(), k_neighbors + 1);

        while (!neighbors.empty()) {
            auto [dist, j] = neighbors.top();
            neighbors.pop();
            if (i == j) continue;
            // Only process each edge once: canonical ordering (smaller index first)
            // This cuts work in half and avoids duplicate A→B / B→A edges
            if (i > j) continue;
            if (token_to_comp.find(vocab[j]) == token_to_comp.end()) continue;

            double sim = 1.0 / (1.0 + std::sqrt(dist));
            if (sim < config_.embedding_similarity_threshold) continue;

            auto tgt_comp_id = token_to_comp.at(vocab[j]);

            // Canonical relation ID: always order hashes consistently (smaller first)
            // This ensures A→B and B→A produce the same relation ID
            std::vector<uint8_t> rel_data = {0x52}; // 'R'
            if (src_comp_id < tgt_comp_id) {
                rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
                rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
            } else {
                rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
                rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
            }
            auto rel_id = BLAKE3Pipeline::hash(rel_data);

            // Thread-local deduplication for relation/physicality/sequence
            // BUT always record rating (ON CONFLICT accumulates ELO)
            bool is_new_relation = tl.rel_seen.insert(rel_id).second;

            if (is_new_relation) {
                std::vector<uint8_t> rel_phys_data = {0x50};
                rel_phys_data.insert(rel_phys_data.end(), rel_id.begin(), rel_id.end());
                auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data);

                if (tl.phys_seen.insert(rel_phys_id).second) {
                    Eigen::Vector4d pos(0.5, 0.5, 0.5, 0.5);
                    pos.normalize();
                    Eigen::Vector4d hc;
                    for (int k = 0; k < 4; ++k) hc[k] = (pos[k] + 1.0) / 2.0;
                    tl.phys.push_back({rel_phys_id, HilbertCurve4D::encode(hc), pos, ""});
                }

                tl.rel.push_back({rel_id, rel_phys_id});

                for (size_t ord = 0; ord < 2; ++ord) {
                    auto& cid = (ord == 0) ? src_comp_id : tgt_comp_id;
                    std::vector<uint8_t> rs_data = {0x54};
                    rs_data.insert(rs_data.end(), rel_id.begin(), rel_id.end());
                    rs_data.insert(rs_data.end(), cid.begin(), cid.end());
                    uint32_t o = static_cast<uint32_t>(ord);
                    rs_data.insert(rs_data.end(), reinterpret_cast<uint8_t*>(&o),
                                  reinterpret_cast<uint8_t*>(&o) + 4);
                    tl.rel_seq.push_back({BLAKE3Pipeline::hash(rs_data), rel_id, cid, o, 1});
                }

                // Evidence is unique per relation per source (ID includes model_id + rel_id)
                std::vector<uint8_t> ev_data;
                ev_data.insert(ev_data.end(), model_id_.begin(), model_id_.end());
                ev_data.insert(ev_data.end(), rel_id.begin(), rel_id.end());
                tl.ev.push_back({BLAKE3Pipeline::hash(ev_data), model_id_, rel_id, true, sim, 1.0});

                tl.relations_created++;
            }

            // ALWAYS record rating - ON CONFLICT accumulates ELO from multiple observations
            double elo = 800.0 + 1200.0 * sim;
            tl.rating.push_back({rel_id, 1, elo, 32.0});
        }

        // Progress reporting (every 1000 tokens)
        size_t count = ++progress_counter;
        if (count % 1000 == 0) {
            #pragma omp critical
            {
                std::cout << "\r      KNN progress: " << count << "/" << n << " tokens processed..." << std::flush;
            }
        }
    }
    std::cout << "\r      KNN extraction complete (" << n << " tokens).                    " << std::endl;

    // Merge and flush in batches
    std::cout << "    Merging thread-local records..." << std::flush;
    std::vector<PhysicalityRecord> phys_records;
    std::vector<RelationRecord> rel_records;
    std::vector<RelationSequenceRecord> rel_seq_records;
    std::vector<RelationRatingRecord> rating_records;
    std::vector<RelationEvidenceRecord> ev_records;

    // Create stores with safe mode (temp tables) to handle duplicates via ON CONFLICT
    // Safe because we've already deduplicated upstream with thread-local sets, but DB might have them
    // Enable BINARY COPY for performance
    PhysicalityStore phys_store(db_, true);  // Needs temp table - cross-phase duplicates
    RelationStore rel_store(db_, true, true);          // Safe mode, Binary
    RelationSequenceStore rel_seq_store(db_, true, true); // Safe mode, Binary
    RelationRatingStore rating_store(db_, true);        // Needs ON CONFLICT, Binary
    RelationEvidenceStore ev_store(db_);

    auto flush_all = [&]() {
        if (rel_records.empty() && ev_records.empty()) return;
        PostgresConnection::Transaction txn(db_);
        for (auto& r : phys_records) phys_store.store(r);
        phys_store.flush();
        for (auto& r : rel_records) rel_store.store(r);
        rel_store.flush();
        for (auto& r : rel_seq_records) rel_seq_store.store(r);
        rel_seq_store.flush();
        for (auto& r : rating_records) rating_store.store(r);
        rating_store.flush();
        for (auto& r : ev_records) ev_store.store(r);
        ev_store.flush();
        txn.commit();
        phys_records.clear(); rel_records.clear(); rel_seq_records.clear(); rating_records.clear(); ev_records.clear();
    };

    const size_t BATCH_SIZE = config_.db_batch_size;
    for (auto& tl : thread_records) {
        // Merge seen relations into session cache
        session_rel_seen.insert(tl.rel_seen.begin(), tl.rel_seen.end());

        phys_records.insert(phys_records.end(), tl.phys.begin(), tl.phys.end());
        rel_records.insert(rel_records.end(), tl.rel.begin(), tl.rel.end());
        rel_seq_records.insert(rel_seq_records.end(), tl.rel_seq.begin(), tl.rel_seq.end());
        rating_records.insert(rating_records.end(), tl.rating.begin(), tl.rating.end());
        ev_records.insert(ev_records.end(), tl.ev.begin(), tl.ev.end());
        stats.relations_created += tl.relations_created;

        if (rel_records.size() >= BATCH_SIZE) {
            std::cout << "\r      Relations: " << stats.relations_created << " written..." << std::flush;
            flush_all();
        }
    }
    flush_all();
    std::cout << "\r      Relations: " << stats.relations_created << " total. Done.          " << std::endl;
}



void ModelIngester::extract_attention_edges(
    const std::vector<Eigen::MatrixXd>& attention_weights,
    const std::vector<std::string>& tokens,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    ModelIngestionStats& stats) {

    std::cout << "    Extracting attention edges from " << attention_weights.size() << " matrices..." << std::endl;

    std::vector<RelationRecord> rel_records;
    std::vector<RelationSequenceRecord> rel_seq_records;
    std::vector<RelationRatingRecord> rating_records;
    std::vector<RelationEvidenceRecord> ev_records;
    std::vector<PhysicalityRecord> phys_records;
    
    // Flush helper (same as others but operates on local vectors)
    auto flush_batch = [&]() {
        if (rel_records.empty() && ev_records.empty()) return;
        PostgresConnection::Transaction txn(db_);
        if (!phys_records.empty()) { PhysicalityStore s(db_); for (auto& r : phys_records) s.store(r); s.flush(); }
        if (!rel_records.empty()) { RelationStore s(db_, true, true); for (auto& r : rel_records) s.store(r); s.flush(); }
        if (!rel_seq_records.empty()) { RelationSequenceStore s(db_, true, true); for (auto& r : rel_seq_records) s.store(r); s.flush(); }
        if (!rating_records.empty()) { RelationRatingStore s(db_, true); for (auto& r : rating_records) s.store(r); s.flush(); }
        if (!ev_records.empty()) { RelationEvidenceStore s(db_); for (auto& r : ev_records) s.store(r); s.flush(); }
        txn.commit();
        phys_records.clear(); rel_records.clear(); rel_seq_records.clear(); rating_records.clear(); ev_records.clear();
    };

    double sparsity_threshold = 0.01;
    constexpr size_t BATCH_SIZE = 50000;

    for (size_t layer_head = 0; layer_head < attention_weights.size(); ++layer_head) {
        const auto& attn = attention_weights[layer_head];
        
        int limit = std::min((int)attn.rows(), (int)tokens.size());
        
        // Single-threaded extraction for this matrix (to keep simplicity as requested, avoiding thread-local overhead for now)
        for (int i = 0; i < limit; ++i) {
            if (token_to_comp.find(tokens[i]) == token_to_comp.end()) continue;
            auto src_comp_id = token_to_comp.at(tokens[i]);

            for (int j = 0; j < limit; ++j) {
                double weight = attn(i, j);
                if (weight < sparsity_threshold) continue;
                if (token_to_comp.find(tokens[j]) == token_to_comp.end()) continue;

                auto tgt_comp_id = token_to_comp.at(tokens[j]);

                // Relation: source token attends to target token
                std::vector<uint8_t> rel_data = {0x52}; // 'R'
                rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
                rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
                auto rel_id = BLAKE3Pipeline::hash(rel_data);

                std::vector<uint8_t> rel_phys_data = {0x50};
                rel_phys_data.insert(rel_phys_data.end(), rel_id.begin(), rel_id.end());
                auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data);

                // Note: we can't easily check 'seen' efficiently here without cache, so rely on DB conflict
                // For physicality, we need positions. Simplified to origin for now as per previous code
                Eigen::Vector4d pos(0.5, 0.5, 0.5, 0.5);
                pos.normalize();
                Eigen::Vector4d hc;
                for (int k = 0; k < 4; ++k) hc[k] = (pos[k] + 1.0) / 2.0;
                phys_records.push_back({rel_phys_id, HilbertCurve4D::encode(hc), pos, ""});

                rel_records.push_back({rel_id, rel_phys_id});

                // Relation sequence
                for (size_t ord = 0; ord < 2; ++ord) {
                    auto& cid = (ord == 0) ? src_comp_id : tgt_comp_id;
                    std::vector<uint8_t> rs_data = {0x54};
                    rs_data.insert(rs_data.end(), rel_id.begin(), rel_id.end());
                    rs_data.insert(rs_data.end(), cid.begin(), cid.end());
                    uint32_t o = static_cast<uint32_t>(ord);
                    rs_data.insert(rs_data.end(), reinterpret_cast<uint8_t*>(&o),
                                  reinterpret_cast<uint8_t*>(&o) + 4);
                    rel_seq_records.push_back({
                        BLAKE3Pipeline::hash(rs_data),
                        rel_id,
                        cid,
                        o,
                        1
                    });
                }

                // ELO from attention weight: [0,1] → [1000,2000]
                double elo = 1000.0 + 1000.0 * weight;
                rating_records.push_back({rel_id, 1, elo, 32.0});
                stats.relations_created++;

                // Evidence from this attention head
                std::vector<uint8_t> ev_data;
                ev_data.insert(ev_data.end(), model_id_.begin(), model_id_.end());
                ev_data.insert(ev_data.end(), rel_id.begin(), rel_id.end());
                uint32_t lh = static_cast<uint32_t>(layer_head);
                ev_data.insert(ev_data.end(), reinterpret_cast<uint8_t*>(&lh),
                              reinterpret_cast<uint8_t*>(&lh) + 4);
                ev_records.push_back({
                    BLAKE3Pipeline::hash(ev_data),
                    model_id_,
                    rel_id,
                    true,
                    weight,
                    1.0
                });
            }
        }
        
        if (rel_records.size() >= BATCH_SIZE) {
            flush_batch();
        }
    }

    // Flush remaining
    flush_batch();

    std::cout << "    Created " << rel_records.size() << " attention relations." << std::endl;
}

void ModelIngester::extract_attention_layer_edges(
    const std::vector<AttentionLayer>& layers,
    const std::vector<std::string>& vocab,
    const Eigen::MatrixXf& embeddings,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2 || layers.empty()) return;

    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);
    const size_t BATCH_SIZE = config_.db_batch_size;

    // Create stores with SAFE mode (temp tables) to handle duplicates
    // Enable BINARY COPY for performance
    PhysicalityStore phys_store(db_, true);  // Needs temp table - cross-phase duplicates
    RelationStore rel_store(db_, true, true);      // Safe mode, Binary
    RelationSequenceStore rel_seq_store(db_, true, true); // Safe mode, Binary
    RelationRatingStore rating_store(db_, true);        // Needs temp table for ON CONFLICT rating updates, Binary
    RelationEvidenceStore ev_store(db_, false);

    for (const auto& layer : layers) {
        if (!layer.q_weight || !layer.k_weight) continue;
        if (layer.q_weight->data.empty() || layer.k_weight->data.empty()) continue;

        auto& q_tensor = *layer.q_weight;
        auto& k_tensor = *layer.k_weight;

        // Q and K are typically [hidden_size, hidden_size] or [hidden_size, num_heads*head_dim]
        // We need the dimension matching embedding_dim
        size_t embed_dim = embeddings.cols();
        size_t q_rows = q_tensor.shape.size() > 0 ? q_tensor.shape[0] : 0;
        size_t q_cols = q_tensor.shape.size() > 1 ? q_tensor.shape[1] : q_tensor.total_elements() / q_rows;

        if (q_rows == 0 || q_cols != embed_dim) {
            // Try transposed interpretation
            if (q_cols == 0 || q_rows != embed_dim) continue;
            std::swap(q_rows, q_cols);
        }

        // Map tensor data to Eigen matrix
        Eigen::Map<const Eigen::MatrixXf> Q(q_tensor.data.data(), q_rows, q_cols);
        Eigen::Map<const Eigen::MatrixXf> K(k_tensor.data.data(),
            k_tensor.shape.size() > 0 ? k_tensor.shape[0] : q_rows,
            k_tensor.shape.size() > 1 ? k_tensor.shape[1] : q_cols);

        // Project embeddings through Q and K
        Eigen::MatrixXf Q_proj = embeddings * Q.transpose();  // [vocab_size, q_out_dim]
        Eigen::MatrixXf K_proj = embeddings * K.transpose();  // [vocab_size, k_out_dim]

        // Normalize projections
        Q_proj.rowwise().normalize();
        K_proj.rowwise().normalize();

        // Build HNSW on K projections - find which tokens offer what token i queries for
        size_t proj_dim = Q_proj.cols();
        hnswlib::L2Space space(proj_dim);
        hnswlib::HierarchicalNSW<float> index(&space, n, 16, 200);
        
        #pragma omp parallel for schedule(dynamic, 512)
        for (size_t i = 0; i < n; ++i) {
            index.addPoint(K_proj.row(i).data(), i);
        }
        index.setEf(config_.hnsw_ef_search);

        int num_threads = omp_get_max_threads();
        std::vector<ThreadLocalRecords> thread_records(num_threads);

        #pragma omp parallel for schedule(dynamic, 128)
        for (size_t i = 0; i < n; ++i) {
            int tid = omp_get_thread_num();
            auto& tl = thread_records[tid];
            if (token_to_comp.find(vocab[i]) == token_to_comp.end()) continue;
            auto src_comp_id = token_to_comp.at(vocab[i]);

            // Query with Q projection to find matching K projections
            auto neighbors = index.searchKnn(Q_proj.row(i).data(), k_neighbors + 1);

            while (!neighbors.empty()) {
                auto [dist, j] = neighbors.top();
                neighbors.pop();
                if (i == j) continue;
                // Canonical ordering: only process each edge once
                if (i > j) continue;
                if (token_to_comp.find(vocab[j]) == token_to_comp.end()) continue;

                double sim = 1.0 / (1.0 + std::sqrt(dist));
                if (sim < config_.embedding_similarity_threshold) continue;

                auto tgt_comp_id = token_to_comp.at(vocab[j]);

                // Canonical relation ID: smaller hash first
                std::vector<uint8_t> rel_data = {0x52}; // 'R'
                if (src_comp_id < tgt_comp_id) {
                    rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
                    rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
                } else {
                    rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
                    rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
                }
                auto rel_id = BLAKE3Pipeline::hash(rel_data);

                // Thread-local deduplication for relation/physicality/sequence
                // BUT always record rating (ON CONFLICT accumulates ELO)
                bool is_locally_new = tl.rel_seen.insert(rel_id).second;
                bool is_globally_new = (session_rel_seen.find(rel_id) == session_rel_seen.end());

                if (is_locally_new && is_globally_new) {
                    std::vector<uint8_t> rel_phys_data = {0x50};
                    rel_phys_data.insert(rel_phys_data.end(), rel_id.begin(), rel_id.end());
                    auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data);

                    if (tl.phys_seen.insert(rel_phys_id).second) {
                        Eigen::Vector4d pos(0.5, 0.5, 0.5, 0.5);
                        pos.normalize();
                        Eigen::Vector4d hc;
                        for (int k = 0; k < 4; ++k) hc[k] = (pos[k] + 1.0) / 2.0;
                        tl.phys.push_back({rel_phys_id, HilbertCurve4D::encode(hc), pos, ""});
                    }

                    tl.rel.push_back({rel_id, rel_phys_id});

                    // Relation sequence
                    for (size_t ord = 0; ord < 2; ++ord) {
                        auto& cid = (ord == 0) ? src_comp_id : tgt_comp_id;
                        std::vector<uint8_t> rs_data = {0x54};
                        rs_data.insert(rs_data.end(), rel_id.begin(), rel_id.end());
                        rs_data.insert(rs_data.end(), cid.begin(), cid.end());
                        uint32_t o = static_cast<uint32_t>(ord);
                        rs_data.insert(rs_data.end(), reinterpret_cast<uint8_t*>(&o),
                                      reinterpret_cast<uint8_t*>(&o) + 4);
                        tl.rel_seq.push_back({
                            BLAKE3Pipeline::hash(rs_data),
                            rel_id,
                            cid,
                            o,
                            1
                        });
                    }

                    // Evidence from this attention layer (unique ID includes layer_index)
                    std::vector<uint8_t> ev_data;
                    ev_data.insert(ev_data.end(), model_id_.begin(), model_id_.end());
                    ev_data.insert(ev_data.end(), rel_id.begin(), rel_id.end());
                    uint32_t layer_idx = static_cast<uint32_t>(layer.layer_index);
                    ev_data.insert(ev_data.end(), reinterpret_cast<uint8_t*>(&layer_idx),
                                  reinterpret_cast<uint8_t*>(&layer_idx) + 4);
                    tl.ev.push_back({
                        BLAKE3Pipeline::hash(ev_data),
                        model_id_,
                        rel_id,
                        true,
                        sim,
                        1.0
                    });

                    tl.relations_created++;
                }

                // ALWAYS record rating - ON CONFLICT accumulates ELO
                double elo = 1000.0 + 1000.0 * (sim - config_.embedding_similarity_threshold) /
                             (1.0 - config_.embedding_similarity_threshold);
                tl.rating.push_back({rel_id, 1, elo, 32.0});
            }
        }

        // Merge and flush using pre-created stores
        std::vector<PhysicalityRecord> phys_records;
        std::vector<RelationRecord> rel_records;
        std::vector<RelationSequenceRecord> rel_seq_records;
        std::vector<RelationRatingRecord> rating_records;
        std::vector<RelationEvidenceRecord> ev_records;

        auto flush_all = [&]() {
            if (rel_records.empty() && ev_records.empty()) return;
            PostgresConnection::Transaction txn(db_);
            for (auto& r : phys_records) phys_store.store(r);
            phys_store.flush();
            for (auto& r : rel_records) rel_store.store(r);
            rel_store.flush();
            for (auto& r : rel_seq_records) rel_seq_store.store(r);
            rel_seq_store.flush();
            for (auto& r : rating_records) rating_store.store(r);
            rating_store.flush();
            for (auto& r : ev_records) ev_store.store(r);
            ev_store.flush();
            txn.commit();
            phys_records.clear(); rel_records.clear(); rel_seq_records.clear(); rating_records.clear(); ev_records.clear();
        };

        for (auto& tl : thread_records) {
            session_rel_seen.insert(tl.rel_seen.begin(), tl.rel_seen.end());

            phys_records.insert(phys_records.end(), tl.phys.begin(), tl.phys.end());
            rel_records.insert(rel_records.end(), tl.rel.begin(), tl.rel.end());
            rel_seq_records.insert(rel_seq_records.end(), tl.rel_seq.begin(), tl.rel_seq.end());
            rating_records.insert(rating_records.end(), tl.rating.begin(), tl.rating.end());
            ev_records.insert(ev_records.end(), tl.ev.begin(), tl.ev.end());
            stats.relations_created += tl.relations_created;

            if (rel_records.size() >= BATCH_SIZE) {
                flush_all();
            }
        }
        flush_all();

        layer_count++;
        std::cout << "      Layer " << layer.layer_index << ": processed" << std::endl;
    }

    std::cout << "    Created " << (stats.relations_created - start_relations) << " attention layer relations from "
              << layer_count << " layers." << std::endl;
}

void ModelIngester::extract_ffn_layer_edges(
    const std::vector<FFNLayer>& layers,
    const std::vector<std::string>& vocab,
    const Eigen::MatrixXf& embeddings,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2 || layers.empty()) return;

    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);
    const size_t BATCH_SIZE = config_.db_batch_size;

    // Create stores with SAFE mode (temp tables) to handle duplicates
    // Enable BINARY COPY for performance
    PhysicalityStore phys_store(db_, true);  // Needs temp table - cross-phase duplicates
    RelationStore rel_store(db_, true, true);      // Safe mode, Binary
    RelationSequenceStore rel_seq_store(db_, true, true); // Safe mode, Binary
    RelationRatingStore rating_store(db_, true);        // Needs temp table for ON CONFLICT rating updates, Binary
    RelationEvidenceStore ev_store(db_, false);

    size_t layer_count = 0;
    size_t start_relations = stats.relations_created;

    for (const auto& layer : layers) {
        // Use gate_weight if available (SwiGLU/GeGLU), otherwise up_weight
        const TensorData* proj_tensor = layer.gate_weight ? layer.gate_weight : layer.up_weight;
        if (!proj_tensor || proj_tensor->data.empty()) continue;

        size_t embed_dim = embeddings.cols();
        size_t proj_rows = proj_tensor->shape.size() > 0 ? proj_tensor->shape[0] : 0;
        size_t proj_cols = proj_tensor->shape.size() > 1 ? proj_tensor->shape[1] :
                          proj_tensor->total_elements() / proj_rows;

        // Match dimensions - FFN typically has [intermediate_size, hidden_size]
        bool transposed = false;
        if (proj_cols != embed_dim) {
            if (proj_rows == embed_dim) {
                std::swap(proj_rows, proj_cols);
                transposed = true;
            } else {
                continue; // Dimension mismatch
            }
        }

        // Project embeddings through FFN
        Eigen::Map<const Eigen::MatrixXf> W(proj_tensor->data.data(),
            transposed ? proj_cols : proj_rows,
            transposed ? proj_rows : proj_cols);

        Eigen::MatrixXf activations;
        if (transposed) {
            activations = embeddings * W;  // [vocab_size, intermediate_size]
        } else {
            activations = embeddings * W.transpose();
        }

        // Apply ReLU to get activation pattern (simplified - actual models use SiLU/GELU)
        activations = activations.cwiseMax(0);

        // Normalize
        activations.rowwise().normalize();

        // Build HNSW on activation patterns
        size_t act_dim = activations.cols();
        hnswlib::L2Space space(act_dim);
        hnswlib::HierarchicalNSW<float> index(&space, n, 16, 200);
        
        #pragma omp parallel for schedule(dynamic, 512)
        for (size_t i = 0; i < n; ++i) {
            index.addPoint(activations.row(i).data(), i);
        }
        index.setEf(config_.hnsw_ef_search);

        int num_threads = omp_get_max_threads();
        std::vector<ThreadLocalRecords> thread_records(num_threads);

        #pragma omp parallel for schedule(dynamic, 128)
        for (size_t i = 0; i < n; ++i) {
            int tid = omp_get_thread_num();
            auto& tl = thread_records[tid];
            if (token_to_comp.find(vocab[i]) == token_to_comp.end()) continue;
            auto src_comp_id = token_to_comp.at(vocab[i]);

            auto neighbors = index.searchKnn(activations.row(i).data(), k_neighbors + 1);

            while (!neighbors.empty()) {
                auto [dist, j] = neighbors.top();
                neighbors.pop();
                if (i == j) continue;
                // Canonical ordering: only process each edge once
                if (i > j) continue;
                if (token_to_comp.find(vocab[j]) == token_to_comp.end()) continue;

                double sim = 1.0 / (1.0 + std::sqrt(dist));
                if (sim < config_.embedding_similarity_threshold) continue;

                auto tgt_comp_id = token_to_comp.at(vocab[j]);

                // Canonical relation ID: smaller hash first
                std::vector<uint8_t> rel_data = {0x52};
                if (src_comp_id < tgt_comp_id) {
                    rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
                    rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
                } else {
                    rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
                    rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
                }
                auto rel_id = BLAKE3Pipeline::hash(rel_data);

                // Thread-local deduplication for relation/physicality/sequence
                // BUT always record rating (ON CONFLICT accumulates ELO)
                bool is_locally_new = tl.rel_seen.insert(rel_id).second;
                bool is_globally_new = (session_rel_seen.find(rel_id) == session_rel_seen.end());

                if (is_locally_new && is_globally_new) {
                    std::vector<uint8_t> rel_phys_data = {0x50};
                    rel_phys_data.insert(rel_phys_data.end(), rel_id.begin(), rel_id.end());
                    auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data);

                    if (tl.phys_seen.insert(rel_phys_id).second) {
                        Eigen::Vector4d pos(0.5, 0.5, 0.5, 0.5);
                        pos.normalize();
                        Eigen::Vector4d hc;
                        for (int k = 0; k < 4; ++k) hc[k] = (pos[k] + 1.0) / 2.0;
                        tl.phys.push_back({rel_phys_id, HilbertCurve4D::encode(hc), pos, ""});
                    }

                    tl.rel.push_back({rel_id, rel_phys_id});

                    // Relation sequence
                    for (size_t ord = 0; ord < 2; ++ord) {
                        auto& cid = (ord == 0) ? src_comp_id : tgt_comp_id;
                        std::vector<uint8_t> rs_data = {0x54};
                        rs_data.insert(rs_data.end(), rel_id.begin(), rel_id.end());
                        rs_data.insert(rs_data.end(), cid.begin(), cid.end());
                        uint32_t o = static_cast<uint32_t>(ord);
                        rs_data.insert(rs_data.end(), reinterpret_cast<uint8_t*>(&o),
                                      reinterpret_cast<uint8_t*>(&o) + 4);
                        tl.rel_seq.push_back({
                            BLAKE3Pipeline::hash(rs_data),
                            rel_id,
                            cid,
                            o,
                            1
                        });
                    }

                    // Evidence from this FFN layer (unique ID includes layer_index + 0xFF marker)
                    std::vector<uint8_t> ev_data;
                    ev_data.insert(ev_data.end(), model_id_.begin(), model_id_.end());
                    ev_data.insert(ev_data.end(), rel_id.begin(), rel_id.end());
                    uint32_t layer_idx = static_cast<uint32_t>(layer.layer_index);
                    ev_data.insert(ev_data.end(), reinterpret_cast<uint8_t*>(&layer_idx),
                                  reinterpret_cast<uint8_t*>(&layer_idx) + 4);
                    ev_data.push_back(0xFF); // Marker to differentiate from attention evidence
                    tl.ev.push_back({
                        BLAKE3Pipeline::hash(ev_data),
                        model_id_,
                        rel_id,
                        true,
                        sim,
                        1.0
                    });

                    tl.relations_created++;
                }

                // ALWAYS record rating - ON CONFLICT accumulates ELO
                double elo = 1000.0 + 1000.0 * (sim - config_.embedding_similarity_threshold) /
                             (1.0 - config_.embedding_similarity_threshold);
                tl.rating.push_back({rel_id, 1, elo, 32.0});
            }
        }

        // Merge and flush using pre-created stores
        std::vector<PhysicalityRecord> phys_records;
        std::vector<RelationRecord> rel_records;
        std::vector<RelationSequenceRecord> rel_seq_records;
        std::vector<RelationRatingRecord> rating_records;
        std::vector<RelationEvidenceRecord> ev_records;

        auto flush_all = [&]() {
            if (rel_records.empty() && ev_records.empty()) return;
            PostgresConnection::Transaction txn(db_);
            for (auto& r : phys_records) phys_store.store(r);
            phys_store.flush();
            for (auto& r : rel_records) rel_store.store(r);
            rel_store.flush();
            for (auto& r : rel_seq_records) rel_seq_store.store(r);
            rel_seq_store.flush();
            for (auto& r : rating_records) rating_store.store(r);
            rating_store.flush();
            for (auto& r : ev_records) ev_store.store(r);
            ev_store.flush();
            txn.commit();
            phys_records.clear(); rel_records.clear(); rel_seq_records.clear(); rating_records.clear(); ev_records.clear();
        };

        for (auto& tl : thread_records) {
            session_rel_seen.insert(tl.rel_seen.begin(), tl.rel_seen.end());

            phys_records.insert(phys_records.end(), tl.phys.begin(), tl.phys.end());
            rel_records.insert(rel_records.end(), tl.rel.begin(), tl.rel.end());
            rel_seq_records.insert(rel_seq_records.end(), tl.rel_seq.begin(), tl.rel_seq.end());
            rating_records.insert(rating_records.end(), tl.rating.begin(), tl.rating.end());
            ev_records.insert(ev_records.end(), tl.ev.begin(), tl.ev.end());
            stats.relations_created += tl.relations_created;

            if (rel_records.size() >= BATCH_SIZE) {
                flush_all();
            }
        }
        flush_all();

        layer_count++;
        std::cout << "      FFN Layer " << layer.layer_index << ": processed" << std::endl;
    }

    std::cout << "    Created " << (stats.relations_created - start_relations) << " FFN layer relations from "
              << layer_count << " layers." << std::endl;
}

void ModelIngester::ingest_tensor(const std::string& name, const TensorData& tensor, ModelIngestionStats& stats) {
    // Tensors encode transformations between token representations
    // Instead of treating weights as atoms, we should extract the
    // strongest connections as Relations
    //
    // For now, just track tensor metadata
    stats.tensors_processed++;
    std::cout << "    Tensor: " << name << " (" << tensor.data.size() << " params)" << std::endl;

    // TODO: Extract weight connections as relations
    // - For embedding weights: link vocab indices to hidden dimensions
    // - For attention weights: link query/key/value projections
    // - For FFN weights: link layer transformations
}

std::string ModelIngester::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

} // namespace Hartonomous