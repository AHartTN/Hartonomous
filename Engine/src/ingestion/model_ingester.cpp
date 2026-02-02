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
#include <ml/model_extraction.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <iostream>
#include <iomanip>
#include <sstream>
#include <cmath>
#include <algorithm>

namespace Hartonomous {

using namespace hartonomous::spatial;
using namespace hartonomous::ml;

ModelIngester::ModelIngester(PostgresConnection& db, const ModelIngestionConfig& config)
    : db_(db), config_(config) {
    // Model ID = hash of model identifier
    std::vector<uint8_t> id_data;
    id_data.push_back(0x4D); // 'M' for Model
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    model_id_ = BLAKE3Pipeline::hash(id_data);
}

void ModelIngester::load_global_caches() {
    std::cout << "Loading global caches for model ingestion..." << std::endl;
    db_.query("SELECT id FROM hartonomous.physicality", [&](const std::vector<std::string>& row) {
        seen_physicality_ids_.insert(BLAKE3Pipeline::from_hex(row[0]));
    });
    db_.query("SELECT id FROM hartonomous.atom", [&](const std::vector<std::string>& row) {
        seen_atom_ids_.insert(BLAKE3Pipeline::from_hex(row[0]));
    });
    db_.query("SELECT id FROM hartonomous.composition", [&](const std::vector<std::string>& row) {
        seen_composition_ids_.insert(BLAKE3Pipeline::from_hex(row[0]));
    });
    db_.query("SELECT id FROM hartonomous.relation", [&](const std::vector<std::string>& row) {
        seen_relation_ids_.insert(BLAKE3Pipeline::from_hex(row[0]));
    });
    std::cout << "  Loaded: " << seen_composition_ids_.size() << " compositions, "
              << seen_relation_ids_.size() << " relations." << std::endl;
}

ModelIngestionStats ModelIngester::ingest_package(const std::filesystem::path& package_dir) {
    ModelIngestionStats stats;
    if (seen_physicality_ids_.empty()) load_global_caches();

    try {
        SafetensorLoader loader(package_dir.string());
        auto& metadata = loader.metadata();
        stats.vocab_tokens = metadata.vocab.size();
        std::cout << "Ingesting model with " << stats.vocab_tokens << " vocab tokens..." << std::endl;

        PostgresConnection::Transaction txn(db_);

        // 1. Ingest vocab tokens as compositions (SAME PIPELINE AS TEXT)
        auto token_to_comp = ingest_vocab_as_text(metadata.vocab, stats);

        // 2. Extract semantic edges from embeddings
        auto embeddings = loader.get_embeddings();
        if (embeddings.rows() > 0) {
            std::cout << "  Extracting semantic edges from embeddings ("
                      << embeddings.rows() << "x" << embeddings.cols() << ")..." << std::endl;
            extract_embedding_edges(metadata.vocab, embeddings, token_to_comp, stats);
        }

        // 3. Extract attention edges if available
        // TODO: Load attention weights from safetensor and extract
        // auto attention = loader.get_attention_weights();
        // if (!attention.empty()) {
        //     extract_attention_edges(attention, token_to_comp, stats);
        // }

        txn.commit();
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

    // Load atoms for all unique codepoints in vocab
    AtomLookup atom_lookup(db_);
    std::unordered_set<uint32_t> unique_cps;
    for (const auto& token : vocab) {
        for (size_t i = 0; i < token.size(); ) {
            uint8_t c = token[i];
            char32_t cp = 0;
            size_t len = 1;
            if (c < 0x80) { cp = c; }
            else if ((c >> 5) == 0x6) { cp = c & 0x1F; len = 2; }
            else if ((c >> 4) == 0xE) { cp = c & 0x0F; len = 3; }
            else if ((c >> 3) == 0x1E) { cp = c & 0x07; len = 4; }
            for (size_t j = 1; j < len && i + j < token.size(); ++j) {
                uint8_t cc = token[i + j];
                if ((cc >> 6) == 0x2) cp = (cp << 6) | (cc & 0x3F);
            }
            unique_cps.insert(cp);
            i += len;
        }
    }
    std::cout << "    Looking up " << unique_cps.size() << " unique codepoints..." << std::flush;
    auto atom_map = atom_lookup.lookup_batch({unique_cps.begin(), unique_cps.end()});
    std::cout << " found " << atom_map.size() << " atoms." << std::endl;

    // Debug: Show which codepoints are missing
    if (atom_map.size() < unique_cps.size()) {
        std::cout << "    Warning: " << (unique_cps.size() - atom_map.size()) << " codepoints have no atoms." << std::endl;
    }

    // Collect records
    std::vector<PhysicalityRecord> phys_records;
    std::vector<CompositionRecord> comp_records;
    std::vector<CompositionSequenceRecord> seq_records;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;

    for (const auto& token : vocab) {
        // Decode token to codepoints and get atom IDs
        std::vector<BLAKE3Pipeline::Hash> atom_ids;
        std::vector<Eigen::Vector4d> positions;

        for (size_t i = 0; i < token.size(); ) {
            uint8_t c = token[i];
            char32_t cp = 0;
            size_t len = 1;
            if (c < 0x80) { cp = c; }
            else if ((c >> 5) == 0x6) { cp = c & 0x1F; len = 2; }
            else if ((c >> 4) == 0xE) { cp = c & 0x0F; len = 3; }
            else if ((c >> 3) == 0x1E) { cp = c & 0x07; len = 4; }
            for (size_t j = 1; j < len && i + j < token.size(); ++j) {
                uint8_t cc = token[i + j];
                if ((cc >> 6) == 0x2) cp = (cp << 6) | (cc & 0x3F);
            }
            auto it = atom_map.find(cp);
            if (it != atom_map.end()) {
                atom_ids.push_back(it->second.id);
                positions.push_back(it->second.position);
            }
            i += len;
        }

        if (atom_ids.empty()) continue;

        // Composition ID = hash of atom sequence (SAME AS TEXT INGESTION)
        std::vector<uint8_t> comp_data = {0x43}; // 'C' for Composition
        for (const auto& aid : atom_ids) {
            comp_data.insert(comp_data.end(), aid.begin(), aid.end());
        }
        auto comp_id = BLAKE3Pipeline::hash(comp_data);
        token_to_comp[token] = comp_id;

        if (seen_composition_ids_.insert(comp_id).second) {
            // Compute centroid from atom positions
            Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
            for (const auto& p : positions) centroid += p;
            centroid /= static_cast<double>(positions.size());
            double norm = centroid.norm();
            if (norm > 1e-10) centroid /= norm;
            else centroid = Eigen::Vector4d(1, 0, 0, 0);

            // Physicality from centroid
            std::vector<uint8_t> phys_data = {0x50};
            phys_data.insert(phys_data.end(),
                reinterpret_cast<const uint8_t*>(centroid.data()),
                reinterpret_cast<const uint8_t*>(centroid.data()) + 32);
            auto phys_id = BLAKE3Pipeline::hash(phys_data);

            if (phys_seen.insert(phys_id).second) {
                Eigen::Vector4d hc;
                for (int k = 0; k < 4; ++k) hc[k] = (centroid[k] + 1.0) / 2.0;
                phys_records.push_back({phys_id, HilbertCurve4D::encode(hc), centroid});
            }

            comp_records.push_back({comp_id, phys_id});
            stats.compositions_created++;

            // Composition sequence with RLE
            for (size_t i = 0; i < atom_ids.size(); ) {
                uint32_t ordinal = static_cast<uint32_t>(i);
                uint32_t occurrences = 1;
                while (i + occurrences < atom_ids.size() &&
                       atom_ids[i + occurrences] == atom_ids[i]) {
                    ++occurrences;
                }

                std::vector<uint8_t> seq_data = {0x53};
                seq_data.insert(seq_data.end(), comp_id.begin(), comp_id.end());
                seq_data.insert(seq_data.end(), atom_ids[i].begin(), atom_ids[i].end());
                seq_data.insert(seq_data.end(), reinterpret_cast<uint8_t*>(&ordinal),
                               reinterpret_cast<uint8_t*>(&ordinal) + 4);
                seq_records.push_back({
                    BLAKE3Pipeline::hash(seq_data),
                    comp_id,
                    atom_ids[i],
                    ordinal,
                    occurrences
                });
                i += occurrences;
            }
        }
    }

    // Store sequentially with error tracking
    try {
        if (!phys_records.empty()) {
            std::cout << "    Storing " << phys_records.size() << " physicalities..." << std::flush;
            PhysicalityStore store(db_);
            for (auto& r : phys_records) store.store(r);
            store.flush();
            std::cout << " done." << std::endl;
        }
    } catch (const std::exception& e) {
        std::cerr << "    Error storing physicalities: " << e.what() << std::endl;
        throw;
    }

    try {
        if (!comp_records.empty()) {
            std::cout << "    Storing " << comp_records.size() << " compositions..." << std::flush;
            CompositionStore store(db_);
            for (auto& r : comp_records) store.store(r);
            store.flush();
            std::cout << " done." << std::endl;
        }
    } catch (const std::exception& e) {
        std::cerr << "    Error storing compositions: " << e.what() << std::endl;
        throw;
    }

    try {
        if (!seq_records.empty()) {
            std::cout << "    Storing " << seq_records.size() << " sequences..." << std::flush;
            CompositionSequenceStore store(db_);
            for (auto& r : seq_records) store.store(r);
            store.flush();
            std::cout << " done." << std::endl;
        }
    } catch (const std::exception& e) {
        std::cerr << "    Error storing sequences: " << e.what() << std::endl;
        throw;
    }

    std::cout << "    Created " << stats.compositions_created << " new compositions." << std::endl;
    return token_to_comp;
}

void ModelIngester::extract_embedding_edges(
    const std::vector<std::string>& vocab,
    const Eigen::MatrixXf& embeddings,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2) return;

    // Build HNSW index for fast KNN
    hnswlib::L2Space space(embeddings.cols());
    hnswlib::HierarchicalNSW<float> index(&space, n, 16, 200);
    for (size_t i = 0; i < n; ++i) {
        index.addPoint(embeddings.row(i).data(), i);
    }

    // Collect relations and evidence
    std::vector<PhysicalityRecord> phys_records;
    std::vector<RelationRecord> rel_records;
    std::vector<RelationSequenceRecord> rel_seq_records;
    std::vector<RelationRatingRecord> rating_records;
    std::vector<RelationEvidenceRecord> ev_records;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;

    size_t k_neighbors = std::min(size_t(20), n - 1);
    std::cout << "    Extracting KNN edges (k=" << k_neighbors << ")..." << std::endl;

    for (size_t i = 0; i < n; ++i) {
        if (token_to_comp.find(vocab[i]) == token_to_comp.end()) continue;
        auto src_comp_id = token_to_comp.at(vocab[i]);

        auto neighbors = index.searchKnn(embeddings.row(i).data(), k_neighbors + 1);

        while (!neighbors.empty()) {
            auto [dist, j] = neighbors.top();
            neighbors.pop();
            if (i == j) continue;
            if (token_to_comp.find(vocab[j]) == token_to_comp.end()) continue;

            double sim = 1.0 / (1.0 + std::sqrt(dist));
            if (sim < config_.embedding_similarity_threshold) continue;

            auto tgt_comp_id = token_to_comp.at(vocab[j]);

            // Relation ID = hash of composition pair
            std::vector<uint8_t> rel_data = {0x52}; // 'R'
            rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
            rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
            auto rel_id = BLAKE3Pipeline::hash(rel_data);

            if (seen_relation_ids_.insert(rel_id).second) {
                // Compute relation physicality from composition physicalities
                // (Would need to look up actual positions - simplified here)
                std::vector<uint8_t> rel_phys_data = {0x50};
                rel_phys_data.insert(rel_phys_data.end(), rel_id.begin(), rel_id.end());
                auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data);

                if (phys_seen.insert(rel_phys_id).second) {
                    // Simplified - would compute from actual positions
                    Eigen::Vector4d pos(0.5, 0.5, 0.5, 0.5);
                    pos.normalize();
                    Eigen::Vector4d hc;
                    for (int k = 0; k < 4; ++k) hc[k] = (pos[k] + 1.0) / 2.0;
                    phys_records.push_back({rel_phys_id, HilbertCurve4D::encode(hc), pos});
                }

                rel_records.push_back({rel_id, rel_phys_id});

                // Relation sequence (relation → compositions)
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

                // ELO rating based on similarity
                double elo = 800.0 + 1200.0 * sim; // Map [0,1] to [800,2000]
                rating_records.push_back({rel_id, 1, elo, 32.0});

                stats.relations_created++;
            }

            // Evidence from this model
            std::vector<uint8_t> ev_data;
            ev_data.insert(ev_data.end(), model_id_.begin(), model_id_.end());
            ev_data.insert(ev_data.end(), rel_id.begin(), rel_id.end());
            ev_records.push_back({
                BLAKE3Pipeline::hash(ev_data),
                model_id_,
                rel_id,
                true,
                sim,
                1.0
            });
        }

        if ((i + 1) % 1000 == 0) {
            std::cout << "      Processed " << (i + 1) << "/" << n << " tokens...\r" << std::flush;
        }
    }
    std::cout << std::endl;

    // Store sequentially
    std::cout << "    Storing " << rel_records.size() << " relations, "
              << ev_records.size() << " evidence records..." << std::endl;

    if (!phys_records.empty()) {
        PhysicalityStore store(db_);
        for (auto& r : phys_records) store.store(r);
        store.flush();
    }
    if (!rel_records.empty()) {
        RelationStore store(db_);
        for (auto& r : rel_records) store.store(r);
        store.flush();
    }
    if (!rel_seq_records.empty()) {
        RelationSequenceStore store(db_);
        for (auto& r : rel_seq_records) store.store(r);
        store.flush();
    }
    if (!rating_records.empty()) {
        RelationRatingStore store(db_);
        for (auto& r : rating_records) store.store(r);
        store.flush();
    }
    if (!ev_records.empty()) {
        RelationEvidenceStore store(db_);
        for (auto& r : ev_records) store.store(r);
        store.flush();
    }
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
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;

    double sparsity_threshold = 0.01; // Only keep attention > 1%

    for (size_t layer_head = 0; layer_head < attention_weights.size(); ++layer_head) {
        const auto& attn = attention_weights[layer_head];

        for (int i = 0; i < attn.rows() && i < static_cast<int>(tokens.size()); ++i) {
            if (token_to_comp.find(tokens[i]) == token_to_comp.end()) continue;
            auto src_comp_id = token_to_comp.at(tokens[i]);

            for (int j = 0; j < attn.cols() && j < static_cast<int>(tokens.size()); ++j) {
                double weight = attn(i, j);
                if (weight < sparsity_threshold) continue;
                if (token_to_comp.find(tokens[j]) == token_to_comp.end()) continue;

                auto tgt_comp_id = token_to_comp.at(tokens[j]);

                // Relation: source token attends to target token
                std::vector<uint8_t> rel_data = {0x52}; // 'R'
                rel_data.insert(rel_data.end(), src_comp_id.begin(), src_comp_id.end());
                rel_data.insert(rel_data.end(), tgt_comp_id.begin(), tgt_comp_id.end());
                auto rel_id = BLAKE3Pipeline::hash(rel_data);

                if (seen_relation_ids_.insert(rel_id).second) {
                    std::vector<uint8_t> rel_phys_data = {0x50};
                    rel_phys_data.insert(rel_phys_data.end(), rel_id.begin(), rel_id.end());
                    auto rel_phys_id = BLAKE3Pipeline::hash(rel_phys_data);

                    if (phys_seen.insert(rel_phys_id).second) {
                        Eigen::Vector4d pos(0.5, 0.5, 0.5, 0.5);
                        pos.normalize();
                        Eigen::Vector4d hc;
                        for (int k = 0; k < 4; ++k) hc[k] = (pos[k] + 1.0) / 2.0;
                        phys_records.push_back({rel_phys_id, HilbertCurve4D::encode(hc), pos});
                    }

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
                }

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
    }

    // Store sequentially
    if (!phys_records.empty()) {
        PhysicalityStore store(db_);
        for (auto& r : phys_records) store.store(r);
        store.flush();
    }
    if (!rel_records.empty()) {
        RelationStore store(db_);
        for (auto& r : rel_records) store.store(r);
        store.flush();
    }
    if (!rel_seq_records.empty()) {
        RelationSequenceStore store(db_);
        for (auto& r : rel_seq_records) store.store(r);
        store.flush();
    }
    if (!rating_records.empty()) {
        RelationRatingStore store(db_);
        for (auto& r : rating_records) store.store(r);
        store.flush();
    }
    if (!ev_records.empty()) {
        RelationEvidenceStore store(db_);
        for (auto& r : ev_records) store.store(r);
        store.flush();
    }

    std::cout << "    Created " << rel_records.size() << " attention relations." << std::endl;
}

// Deprecated - physicality comes from content, not embeddings
BLAKE3Pipeline::Hash ModelIngester::create_physicality_from_unicode(const std::string& text) {
    // This should not be used - vocab tokens use the text ingestion path
    // Keeping for backwards compatibility
    std::vector<uint8_t> data = {0x50};
    data.insert(data.end(), text.begin(), text.end());
    return BLAKE3Pipeline::hash(data);
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
