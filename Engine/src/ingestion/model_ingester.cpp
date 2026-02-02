/**
 * @file model_ingester.cpp
 * @brief AI model package ingestion implementation
 */

#include <ingestion/model_ingester.hpp>
#include <storage/physicality_store.hpp>
#include <storage/atom_store.hpp>
#include <storage/composition_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <unicode/codepoint_projection.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <iostream>
#include <iomanip>
#include <sstream>
#include <cmath>

namespace Hartonomous {

using namespace hartonomous::unicode;
using namespace hartonomous::spatial;

ModelIngester::ModelIngester(PostgresConnection& db, const ModelIngestionConfig& config)
    : db_(db), config_(config) {
    std::vector<uint8_t> id_data;
    id_data.push_back(0x4D); // 'M' for Model
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    model_id_ = BLAKE3Pipeline::hash(id_data);
}

void ModelIngester::load_global_caches() {
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
}

ModelIngestionStats ModelIngester::ingest_package(const std::filesystem::path& package_dir) {
    ModelIngestionStats stats;
    if (seen_physicality_ids_.empty()) load_global_caches();

    try {
        SafetensorLoader loader(package_dir.string());
        auto& metadata = loader.metadata();
        stats.vocab_tokens = metadata.vocab.size();

        PostgresConnection::Transaction txn(db_);

        // 1. Create vocab compositions
        create_vocab_compositions(metadata.vocab, stats);

        // 2. Extract embedding relations
        auto embeddings = loader.get_embeddings();
        if (embeddings.rows() > 0) {
            extract_embedding_relations(metadata.vocab, embeddings, stats);
        }

        // 3. Ingest all tensors (attention, weights, etc.)
        auto names = loader.tensor_names();
        for (const auto& name : names) {
            if (const auto* tensor = loader.get_tensor(name)) {
                ingest_tensor(name, *tensor, stats);
            }
        }

        txn.commit();
    } catch (const std::exception& e) {
        std::cerr << "Model ingestion failed: " << e.what() << std::endl;
    }

    return stats;
}

void ModelIngester::create_vocab_compositions(const std::vector<std::string>& vocab, ModelIngestionStats& stats) {
    CompositionStore comp_store(db_);
    PhysicalityStore phys_store(db_);

    for (const auto& token : vocab) {
        auto comp_id = BLAKE3Pipeline::hash(token);
        if (seen_composition_ids_.count(comp_id)) continue;

        auto phys_id = create_physicality_from_unicode(token);
        comp_store.store({comp_id, phys_id});
        seen_composition_ids_.insert(comp_id);
        stats.compositions_created++;
    }
    comp_store.flush();
}

BLAKE3Pipeline::Hash ModelIngester::create_physicality_from_unicode(const std::string& text) {
    std::vector<Eigen::Vector4d> positions;
    for (size_t i = 0; i < text.size(); ) {
        // Simple UTF-8 decode for physicality centroid
        uint32_t cp = static_cast<uint8_t>(text[i++]);
        auto proj = CodepointProjection::project(static_cast<char32_t>(cp));
        positions.push_back(proj.s3_position);
    }

    Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
    if (!positions.empty()) {
        for (const auto& p : positions) centroid += p;
        centroid /= static_cast<double>(positions.size());
        double n = centroid.norm();
        if (n > 1e-10) centroid /= n;
        else centroid = Eigen::Vector4d(1, 0, 0, 0);
    } else {
        centroid = Eigen::Vector4d(1, 0, 0, 0);
    }

    std::vector<uint8_t> data;
    data.push_back(0x50); // 'P'
    data.insert(data.end(), reinterpret_cast<const uint8_t*>(centroid.data()), 
                reinterpret_cast<const uint8_t*>(centroid.data()) + 4 * sizeof(double));
    auto phys_id = BLAKE3Pipeline::hash(data);

    if (seen_physicality_ids_.insert(phys_id).second) {
        Eigen::Vector4d hc;
        for (int i = 0; i < 4; ++i) hc[i] = (centroid[i] + 1.0) / 2.0;
        auto h_idx = HilbertCurve4D::encode(hc);
        PhysicalityStore(db_).store({phys_id, h_idx, centroid});
    }

    return phys_id;
}

void ModelIngester::extract_embedding_relations(const std::vector<std::string>& vocab,
                                                const Eigen::MatrixXf& embeddings,
                                                ModelIngestionStats& stats) {
    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2) return;

    hnswlib::L2Space space(embeddings.cols());
    hnswlib::HierarchicalNSW<float> index(&space, n, 16, 200);

    for (size_t i = 0; i < n; ++i) {
        index.addPoint(embeddings.row(i).data(), i);
    }

    RelationStore rel_store(db_);
    RelationRatingStore rating_store(db_);
    RelationEvidenceStore ev_store(db_);

    for (size_t i = 0; i < n; ++i) {
        auto neighbors = index.searchKnn(embeddings.row(i).data(), std::min(size_t(20), n));
        auto src_id = BLAKE3Pipeline::hash(vocab[i]);

        while (!neighbors.empty()) {
            auto [dist, j] = neighbors.top();
            neighbors.pop();
            if (i == j) continue;

            double sim = 1.0 / (1.0 + dist);
            if (sim < config_.embedding_similarity_threshold) continue;

            auto tgt_id = BLAKE3Pipeline::hash(vocab[j]);
            
            std::vector<uint8_t> r_data;
            r_data.push_back(0x52); // 'R'
            r_data.insert(r_data.end(), src_id.begin(), src_id.end());
            r_data.insert(r_data.end(), tgt_id.begin(), tgt_id.end());
            auto rel_id = BLAKE3Pipeline::hash(r_data);

            if (seen_relation_ids_.insert(rel_id).second) {
                rel_store.store({rel_id, BLAKE3Pipeline::Hash()}); // Physicality null for now or compute
                rating_store.store({rel_id, 1, 800.0 + 1200.0 * sim, 32.0});
            }

            std::vector<uint8_t> e_data;
            e_data.insert(e_data.end(), model_id_.begin(), model_id_.end());
            e_data.insert(e_data.end(), rel_id.begin(), rel_id.end());
            ev_store.store({BLAKE3Pipeline::hash(e_data), model_id_, rel_id, true, sim, 1.0});
            
            stats.relations_created++;
        }
    }
    rel_store.flush();
    rating_store.flush();
    ev_store.flush();
}

void ModelIngester::ingest_tensor(const std::string& name, const TensorData& tensor, ModelIngestionStats& stats) {
    // 1. Create Atoms for unique weights (Content Addressing)
    AtomStore atom_store(db_);
    PhysicalityStore phys_store(db_);
    
    std::unordered_map<float, BLAKE3Pipeline::Hash> weight_to_atom;
    
    // We sample or process all if small. For massive tensors, we use RLE.
    // The user wants EVERYTHING run-length encoded.
    
    std::vector<BLAKE3Pipeline::Hash> sequence;
    std::vector<uint32_t> counts;
    
    for (float w : tensor.data) {
        BLAKE3Pipeline::Hash atom_id;
        auto it = weight_to_atom.find(w);
        if (it == weight_to_atom.end()) {
            // Create Atom for this weight value
            std::vector<uint8_t> w_data(sizeof(float));
            std::memcpy(w_data.data(), &w, sizeof(float));
            atom_id = BLAKE3Pipeline::hash(w_data);
            
            if (seen_atom_ids_.insert(atom_id).second) {
                // Map weight value to a "codepoint" space for the Atom table
                // Using bit cast as a simple mapping
                int32_t cp;
                std::memcpy(&cp, &w, sizeof(int32_t));
                
                // Create physicality for this weight
                // A single weight value is a 1D position mapped to 4D
                double normalized = std::tanh(w); // S-curve to [-1, 1]
                Eigen::Vector4d pos(normalized, 0, 0, 0); // Simplified
                
                std::vector<uint8_t> p_data;
                p_data.push_back(0x50);
                p_data.insert(p_data.end(), reinterpret_cast<uint8_t*>(pos.data()), reinterpret_cast<uint8_t*>(pos.data()) + 32);
                auto phys_id = BLAKE3Pipeline::hash(p_data);
                
                if (seen_physicality_ids_.insert(phys_id).second) {
                    phys_store.store({phys_id, {0, 0}, pos});
                }
                
                atom_store.store({atom_id, phys_id, static_cast<int32_t>(cp)});
                stats.atoms_created++;
            }
            weight_to_atom[w] = atom_id;
        } else {
            atom_id = it->second;
        }
        
        // Run-Length Encoding
        if (!sequence.empty() && sequence.back() == atom_id) {
            counts.back()++;
        } else {
            sequence.push_back(atom_id);
            counts.push_back(1);
        }
    }
    atom_store.flush();
    phys_store.flush();
    
    // 2. Create Composition for the tensor
    auto tensor_comp_id = BLAKE3Pipeline::hash(name);
    if (seen_composition_ids_.insert(tensor_comp_id).second) {
        CompositionStore(db_).store({tensor_comp_id, BLAKE3Pipeline::Hash()});
        stats.compositions_created++;
    }
    
    // 3. Store Sequence with RLE
    CompositionSequenceStore seq_store(db_);
    for (size_t i = 0; i < sequence.size(); ++i) {
        std::vector<uint8_t> s_data;
        s_data.insert(s_data.end(), tensor_comp_id.begin(), tensor_comp_id.end());
        s_data.insert(s_data.end(), sequence[i].begin(), sequence[i].end());
        uint32_t ord = static_cast<uint32_t>(i);
        s_data.insert(s_data.end(), reinterpret_cast<uint8_t*>(&ord), reinterpret_cast<uint8_t*>(&ord) + 4);
        
        seq_store.store({
            BLAKE3Pipeline::hash(s_data),
            tensor_comp_id,
            sequence[i],
            ord,
            counts[i]
        });
    }
    seq_store.flush();
    stats.tensors_processed++;
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