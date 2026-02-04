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
#include <queue>
#include <omp.h>

namespace Hartonomous {

using namespace hartonomous::spatial;
using namespace hartonomous::ml;

struct ThreadLocalRecords {
    std::vector<PhysicalityRecord> phys;
    std::vector<RelationRecord> rel;
    std::vector<RelationSequenceRecord> rel_seq;
    std::vector<RelationRatingRecord> rating;
    std::vector<RelationEvidenceRecord> ev;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> rel_seen; 
    size_t relations_created = 0;

    void clear() {
        phys.clear(); rel.clear(); rel_seq.clear(); rating.clear(); ev.clear();
        phys_seen.clear(); rel_seen.clear();
        relations_created = 0;
    }
};

ModelIngester::ModelIngester(PostgresConnection& db, const ModelIngestionConfig& config)
    : db_(db), config_(config) {
    std::vector<uint8_t> id_data;
    id_data.push_back(0x4D); 
    id_data.insert(id_data.end(), config_.tenant_id.begin(), config_.tenant_id.end());
    id_data.insert(id_data.end(), config_.user_id.begin(), config_.user_id.end());
    model_id_ = BLAKE3Pipeline::hash(id_data);
}

ModelIngestionStats ModelIngester::ingest_package(const std::filesystem::path& package_dir) {
    ModelIngestionStats stats;
    try {
        SafetensorLoader loader(package_dir.string());
        auto& metadata = loader.metadata();
        stats.vocab_tokens = metadata.vocab.size();
        std::cout << "Ingesting model with " << stats.vocab_tokens << " vocab tokens..." << std::endl;

        std::cout << "  Creating content record for model: " << hash_to_uuid(model_id_) << std::endl;
        
        std::cout << "  Optimizing database for bulk ingestion..." << std::endl;
        
        // Correct order: referencing tables first, then referenced tables.
        // References:
        // RelationEvidence -> Relation, Content
        // RelationRating -> Relation
        // RelationSequence -> Relation, Composition
        // CompositionSequence -> Composition, Atom
        // Relation -> Physicality
        // Composition -> Physicality
        // Atom -> Physicality
        
        db_.execute("ALTER TABLE hartonomous.relationevidence SET UNLOGGED");
        db_.execute("ALTER TABLE hartonomous.relationrating SET UNLOGGED");
        db_.execute("ALTER TABLE hartonomous.relationsequence SET UNLOGGED");
        db_.execute("ALTER TABLE hartonomous.compositionsequence SET UNLOGGED");
        db_.execute("ALTER TABLE hartonomous.relation SET UNLOGGED");
        db_.execute("ALTER TABLE hartonomous.composition SET UNLOGGED");
        db_.execute("ALTER TABLE hartonomous.atom SET UNLOGGED");
        db_.execute("ALTER TABLE hartonomous.physicality SET UNLOGGED");
        
        db_.execute("DROP INDEX IF EXISTS hartonomous.idx_physicality_centroid");
        db_.execute("DROP INDEX IF EXISTS hartonomous.idx_physicality_trajectory");

        try {
            PostgresConnection::Transaction txn(db_);
            ContentStore content_store(db_);
            content_store.store({
                model_id_, config_.tenant_id, config_.user_id, 2, model_id_, 0, 
                "application/octet-stream", "en", package_dir.string(), "binary"
            });
            content_store.flush();
            txn.commit();
        } catch (...) { /* Exists */ }

        auto token_to_comp = ingest_vocab_as_text(metadata.vocab, stats);
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> session_rel_seen;

        auto embeddings = loader.get_embeddings();
        if (embeddings.rows() > 0) {
            extract_embedding_edges(metadata.vocab, embeddings, token_to_comp, session_rel_seen, stats);
        }

        auto attention_layers = loader.get_attention_layers();
        if (!attention_layers.empty() && embeddings.rows() > 0) {
            extract_attention_layer_edges(attention_layers, metadata.vocab, embeddings, token_to_comp, session_rel_seen, stats);
        }

        auto ffn_layers = loader.get_ffn_layers();
        if (!ffn_layers.empty() && embeddings.rows() > 0) {
            extract_ffn_layer_edges(ffn_layers, metadata.vocab, embeddings, token_to_comp, session_rel_seen, stats);
        }

        std::cout << "  Model ingestion complete. Restoring database durability and indexes..." << std::endl;
        db_.execute("CREATE INDEX idx_physicality_centroid ON hartonomous.physicality USING GIST(centroid gist_geometry_ops_nd)");
        db_.execute("CREATE INDEX idx_physicality_trajectory ON hartonomous.physicality USING GIST(trajectory gist_geometry_ops_nd)");
        
        // Correct order for restoring LOGGED status:
        // Referenced tables first, then referencing tables.
        db_.execute("ALTER TABLE hartonomous.physicality SET LOGGED");
        db_.execute("ALTER TABLE hartonomous.atom SET LOGGED");
        db_.execute("ALTER TABLE hartonomous.composition SET LOGGED");
        db_.execute("ALTER TABLE hartonomous.relation SET LOGGED");
        db_.execute("ALTER TABLE hartonomous.compositionsequence SET LOGGED");
        db_.execute("ALTER TABLE hartonomous.relationsequence SET LOGGED");
        db_.execute("ALTER TABLE hartonomous.relationrating SET LOGGED");
        db_.execute("ALTER TABLE hartonomous.relationevidence SET LOGGED");

    } catch (const std::exception& e) {
        std::cerr << "Model ingestion failed: " << e.what() << std::endl;
    }
    return stats;
}

std::unordered_map<std::string, BLAKE3Pipeline::Hash>
ModelIngester::ingest_vocab_as_text(const std::vector<std::string>& vocab, ModelIngestionStats& stats) {
    std::cout << "  Ingesting vocab tokens as compositions..." << std::endl;
    std::unordered_map<std::string, BLAKE3Pipeline::Hash> token_to_comp;

    AtomLookup atom_lookup(db_);
    std::unordered_set<uint32_t> unique_cps;
    for (const auto& token : vocab) {
        for (size_t i = 0; i < token.size(); ) {
            uint8_t c = token[i]; char32_t cp = 0; size_t len = 1;
            if (c < 0x80) cp = c;
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

    int num_threads = omp_get_max_threads();
    struct Local {
        std::vector<PhysicalityRecord> phys;
        std::vector<CompositionRecord> comp;
        std::vector<CompositionSequenceRecord> seq;
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;
        std::vector<std::pair<std::string, BLAKE3Pipeline::Hash>> mappings;
        size_t created = 0;
    };
    std::vector<Local> locals(num_threads);

    #pragma omp parallel for schedule(dynamic, 256)
    for (size_t i = 0; i < vocab.size(); ++i) {
        const auto& token = vocab[i];
        auto& tl = locals[omp_get_thread_num()];
        std::vector<BLAKE3Pipeline::Hash> atom_ids;
        std::vector<Eigen::Vector4d> positions;

        for (size_t k = 0; k < token.size(); ) {
            uint8_t c = token[k]; char32_t cp = 0; size_t len = 1;
            if (c < 0x80) cp = c;
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

        std::vector<uint8_t> cdata = {0x43};
        for (const auto& aid : atom_ids) { cdata.insert(cdata.end(), aid.begin(), aid.end()); }
        auto cid = BLAKE3Pipeline::hash(cdata);
        tl.mappings.push_back({token, cid});

        Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
        for (const auto& p : positions) { centroid += p; }
        centroid /= static_cast<double>(positions.size());
        double nrm = centroid.norm();
        if (nrm > 1e-10) centroid /= nrm; else centroid = Eigen::Vector4d(1,0,0,0);

        std::vector<uint8_t> pdata = {0x50};
        pdata.insert(pdata.end(), reinterpret_cast<const uint8_t*>(centroid.data()), reinterpret_cast<const uint8_t*>(centroid.data()) + 32);
        auto pid = BLAKE3Pipeline::hash(pdata);

        if (tl.phys_seen.insert(pid).second) {
            Eigen::Vector4d hc; for (int k=0; k<4; ++k) { hc[k] = (centroid[k]+1.0)/2.0; }
            tl.phys.push_back({pid, HilbertCurve4D::encode(hc), centroid, positions});
        }
        tl.comp.push_back({cid, pid}); tl.created++;

        for (size_t k = 0; k < atom_ids.size(); ) {
            uint32_t ord = static_cast<uint32_t>(k); uint32_t occ = 1;
            while (k + occ < atom_ids.size() && atom_ids[k + occ] == atom_ids[k]) { ++occ; }
            std::vector<uint8_t> sdata = {0x53};
            sdata.insert(sdata.end(), cid.begin(), cid.end());
            sdata.insert(sdata.end(), atom_ids[k].begin(), atom_ids[k].end());
            sdata.insert(sdata.end(), reinterpret_cast<uint8_t*>(&ord), reinterpret_cast<uint8_t*>(&ord) + 4);
            tl.seq.push_back({BLAKE3Pipeline::hash(sdata), cid, atom_ids[k], ord, occ});
            k += occ;
        }
    }

    PhysicalityStore phys_store(db_, true, true);
    CompositionStore comp_store(db_, true, true);
    CompositionSequenceStore seq_store(db_, true, true);

    for (auto& tl : locals) {
        for (auto& m : tl.mappings) { token_to_comp[m.first] = m.second; }
        PostgresConnection::Transaction txn(db_);
        for (auto& r : tl.phys) { phys_store.store(r); } phys_store.flush();
        for (auto& r : tl.comp) { comp_store.store(r); } comp_store.flush();
        for (auto& r : tl.seq) { seq_store.store(r); } seq_store.flush();
        txn.commit();
        stats.compositions_created += tl.created;
    }
    return token_to_comp;
}

void ModelIngester::extract_embedding_edges(
    const std::vector<std::string>& vocab, const Eigen::MatrixXf& embeddings,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2) return;

    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);
    std::cout << "    Extracting KNN edges via HNSWLib (k=" << k_neighbors << ")..." << std::endl;

    // Build HNSW index
    int dim = static_cast<int>(embeddings.cols());
    hnswlib::InnerProductSpace space(dim);
    hnswlib::HierarchicalNSW<float> index(&space, n, 16, 200);

    // Normalize embeddings for Inner Product (Cosine Similarity)
    Eigen::MatrixXf norm_embeddings = embeddings.topRows(n);
    norm_embeddings.rowwise().normalize();

    std::cout << "      Building HNSW index..." << std::endl;
    #pragma omp parallel for schedule(dynamic, 1024)
    for (size_t i = 0; i < n; ++i) {
        index.addPoint(norm_embeddings.row(i).data(), i);
    }

    int num_threads = omp_get_max_threads();
    std::vector<ThreadLocalRecords> locals(num_threads);

    std::cout << "      Querying HNSW index..." << std::endl;
    #pragma omp parallel for schedule(dynamic, 128)
    for (size_t i = 0; i < n; ++i) {
        auto& tl = locals[omp_get_thread_num()];
        if (token_to_comp.find(vocab[i]) == token_to_comp.end()) continue;
        auto scid = token_to_comp.at(vocab[i]);

        // HNSW search
        auto result_pq = index.searchKnn(norm_embeddings.row(i).data(), k_neighbors + 1);

        while (!result_pq.empty()) {
            auto [dist, j] = result_pq.top();
            result_pq.pop();

            if (i == j) continue;
            float sim = 1.0f - dist; // Inner Product distance is 1.0 - IP
            if (sim < config_.embedding_similarity_threshold) continue;

            if (token_to_comp.find(vocab[j]) == token_to_comp.end()) continue;
            auto tcid = token_to_comp.at(vocab[j]);

            // Ensure consistent ordering for relation hashing
            std::vector<uint8_t> rdata = {0x52};
            if (std::memcmp(scid.data(), tcid.data(), 16) < 0) { 
                rdata.insert(rdata.end(), scid.begin(), scid.end()); 
                rdata.insert(rdata.end(), tcid.begin(), tcid.end()); 
            } else { 
                rdata.insert(rdata.end(), tcid.begin(), tcid.end()); 
                rdata.insert(rdata.end(), scid.begin(), scid.end()); 
            }
            auto rid = BLAKE3Pipeline::hash(rdata);

            bool is_new = tl.rel_seen.insert(rid).second && (session_rel_seen.find(rid) == session_rel_seen.end());
            if (is_new) {
                // ... same as before ...
                std::vector<uint8_t> pdata = {0x50}; pdata.insert(pdata.end(), rid.begin(), rid.end());
                auto prid = BLAKE3Pipeline::hash(pdata);
                if (tl.phys_seen.insert(prid).second) {
                    Eigen::Vector4d pos(0.5,0.5,0.5,0.5); pos.normalize();
                    Eigen::Vector4d hc; for (int k=0; k<4; ++k) { hc[k] = (pos[k]+1.0)/2.0; }
                    tl.phys.push_back({prid, HilbertCurve4D::encode(hc), pos, {}});
                }
                tl.rel.push_back({rid, prid});
                for (size_t ord=0; ord<2; ++ord) {
                    auto& cid = (ord==0)?scid:tcid; std::vector<uint8_t> sdata = {0x54};
                    sdata.insert(sdata.end(), rid.begin(), rid.end()); sdata.insert(sdata.end(), cid.begin(), cid.end());
                    uint32_t o = static_cast<uint32_t>(ord); 
                    sdata.insert(sdata.end(), reinterpret_cast<uint8_t*>(&o), reinterpret_cast<uint8_t*>(&o) + 4);
                    tl.rel_seq.push_back({BLAKE3Pipeline::hash(sdata), rid, cid, o, 1});
                }
                std::vector<uint8_t> edata; 
                edata.insert(edata.end(), model_id_.begin(), model_id_.end()); 
                edata.insert(edata.end(), rid.begin(), rid.end());
                tl.ev.push_back({BLAKE3Pipeline::hash(edata), model_id_, rid, true, (double)sim, 1.0});
                tl.relations_created++;
            }
            tl.rating.push_back({rid, 1, 800.0 + 1200.0 * sim, 32.0});
        }
    }

    PhysicalityStore phys_store(db_, true, true);
    RelationStore rel_store(db_, true, true);
    RelationSequenceStore rel_seq_store(db_, true, true);
    RelationRatingStore rating_store(db_, true);
    RelationEvidenceStore ev_store(db_, false, true);

    for (auto& tl : locals) {
        session_rel_seen.insert(tl.rel_seen.begin(), tl.rel_seen.end());
        PostgresConnection::Transaction txn(db_);
        for (auto& r : tl.phys) { phys_store.store(r); } phys_store.flush();
        for (auto& r : tl.rel) { rel_store.store(r); } rel_store.flush();
        for (auto& r : tl.rel_seq) { rel_seq_store.store(r); } rel_seq_store.flush();
        for (auto& r : tl.rating) { rating_store.store(r); } rating_store.flush();
        for (auto& r : tl.ev) { ev_store.store(r); } ev_store.flush();
        txn.commit();
        stats.relations_created += tl.relations_created;
    }
}

void ModelIngester::extract_attention_layer_edges(
    const std::vector<AttentionLayer>& layers, const std::vector<std::string>& vocab,
    const Eigen::MatrixXf& embeddings, const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2 || layers.empty()) return;
    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);
    size_t start_relations = stats.relations_created;
    size_t layer_count = 0;

    for (const auto& layer : layers) {
        if (!layer.q_weight || !layer.k_weight) continue;
        
        Eigen::Map<const Eigen::MatrixXf> Q(layer.q_weight->data.data(), layer.q_weight->shape[0], layer.q_weight->shape[1]);
        Eigen::Map<const Eigen::MatrixXf> K(layer.k_weight->data.data(), layer.k_weight->shape[0], layer.k_weight->shape[1]);
        
        // Project embeddings to Query/Key space
        Eigen::MatrixXf Qp = embeddings.topRows(n) * Q.transpose(); Qp.rowwise().normalize();
        Eigen::MatrixXf Kp = embeddings.topRows(n) * K.transpose(); Kp.rowwise().normalize();

        // Build HNSW index on Keys
        int dim = static_cast<int>(Kp.cols());
        hnswlib::InnerProductSpace space(dim);
        hnswlib::HierarchicalNSW<float> index(&space, n, 16, 200);

        #pragma omp parallel for schedule(dynamic, 1024)
        for (size_t i = 0; i < n; ++i) {
            index.addPoint(Kp.row(i).data(), i);
        }

        int num_threads = omp_get_max_threads();
        std::vector<ThreadLocalRecords> locals(num_threads);

        // Query with Queries
        #pragma omp parallel for schedule(dynamic, 128)
        for (size_t i = 0; i < n; ++i) {
            auto& tl = locals[omp_get_thread_num()];
            if (token_to_comp.find(vocab[i]) == token_to_comp.end()) continue;
            auto scid = token_to_comp.at(vocab[i]);

            auto result_pq = index.searchKnn(Qp.row(i).data(), k_neighbors + 1);

            while (!result_pq.empty()) {
                auto [dist, j] = result_pq.top();
                result_pq.pop();

                if (i == j) continue;
                float sim = 1.0f - dist;
                if (sim < config_.embedding_similarity_threshold) continue;

                if (token_to_comp.find(vocab[j]) == token_to_comp.end()) continue;
                auto tcid = token_to_comp.at(vocab[j]);

                std::vector<uint8_t> rdata = {0x52};
                if (std::memcmp(scid.data(), tcid.data(), 16) < 0) { 
                    rdata.insert(rdata.end(), scid.begin(), scid.end()); 
                    rdata.insert(rdata.end(), tcid.begin(), tcid.end()); 
                } else { 
                    rdata.insert(rdata.end(), tcid.begin(), tcid.end()); 
                    rdata.insert(rdata.end(), scid.begin(), scid.end()); 
                }
                auto rid = BLAKE3Pipeline::hash(rdata);

                if (tl.rel_seen.insert(rid).second && session_rel_seen.find(rid) == session_rel_seen.end()) {
                    std::vector<uint8_t> pdata = {0x50}; pdata.insert(pdata.end(), rid.begin(), rid.end());
                    auto prid = BLAKE3Pipeline::hash(pdata);
                    if (tl.phys_seen.insert(prid).second) {
                        Eigen::Vector4d pos(0.5,0.5,0.5,0.5); pos.normalize();
                        Eigen::Vector4d hc; for (int k=0; k<4; ++k) { hc[k] = (pos[k]+1.0)/2.0; }
                        tl.phys.push_back({prid, HilbertCurve4D::encode(hc), pos, {}});
                    }
                    tl.rel.push_back({rid, prid});
                    for (size_t ord=0; ord<2; ++ord) {
                        auto& cid = (ord==0)?scid:tcid; std::vector<uint8_t> sdata = {0x54};
                        sdata.insert(sdata.end(), rid.begin(), rid.end()); sdata.insert(sdata.end(), cid.begin(), cid.end());
                        uint32_t o = static_cast<uint32_t>(ord); 
                        sdata.insert(sdata.end(), reinterpret_cast<uint8_t*>(&o), reinterpret_cast<uint8_t*>(&o) + 4);
                        tl.rel_seq.push_back({BLAKE3Pipeline::hash(sdata), rid, cid, o, 1});
                    }
                    std::vector<uint8_t> edata; edata.insert(edata.end(), model_id_.begin(), model_id_.end()); edata.insert(edata.end(), rid.begin(), rid.end());
                    uint32_t lidx = static_cast<uint32_t>(layer.layer_index); 
                    edata.insert(edata.end(), reinterpret_cast<uint8_t*>(&lidx), reinterpret_cast<uint8_t*>(&lidx) + 4);
                    tl.ev.push_back({BLAKE3Pipeline::hash(edata), model_id_, rid, true, (double)sim, 1.0});
                    tl.relations_created++;
                }
                tl.rating.push_back({rid, 1, 1000.0 + 1000.0 * (sim - config_.embedding_similarity_threshold)/(1.0-config_.embedding_similarity_threshold), 32.0});
            }
        }
        PhysicalityStore phys_store(db_, true, true); RelationStore rel_store(db_, true, true); RelationSequenceStore rel_seq_store(db_, true, true);
        RelationRatingStore rating_store(db_, true); RelationEvidenceStore ev_store(db_, false, true);
        for (auto& tl : locals) {
            session_rel_seen.insert(tl.rel_seen.begin(), tl.rel_seen.end());
            PostgresConnection::Transaction txn(db_);
            for (auto& r : tl.phys) { phys_store.store(r); } phys_store.flush();
            for (auto& r : tl.rel) { rel_store.store(r); } rel_store.flush();
            for (auto& r : tl.rel_seq) { rel_seq_store.store(r); } rel_seq_store.flush();
            for (auto& r : tl.rating) { rating_store.store(r); } rating_store.flush();
            for (auto& r : tl.ev) { ev_store.store(r); } ev_store.flush();
            txn.commit(); stats.relations_created += tl.relations_created;
        }
        layer_count++;
        std::cout << "      Layer " << layer.layer_index << ": processed" << std::endl;
    }
    std::cout << "    Created " << (stats.relations_created - start_relations) << " attention relations from " << layer_count << " layers." << std::endl;
}

void ModelIngester::extract_ffn_layer_edges(
    const std::vector<FFNLayer>& layers, const std::vector<std::string>& vocab,
    const Eigen::MatrixXf& embeddings, const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2 || layers.empty()) return;
    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);
    size_t start_relations = stats.relations_created;
    size_t layer_count = 0;

    for (const auto& layer : layers) {
        const TensorData* p_tensor = layer.gate_weight ? layer.gate_weight : layer.up_weight;
        if (!p_tensor || p_tensor->data.empty()) continue;
        
        Eigen::Map<const Eigen::MatrixXf> W(p_tensor->data.data(), p_tensor->shape[0], p_tensor->shape[1]);
        
        // Calculate neuron activations for each token
        Eigen::MatrixXf acts = (embeddings.topRows(n) * W.transpose()).cwiseMax(0); 
        acts.rowwise().normalize();

        // Build HNSW index on activations
        int dim = static_cast<int>(acts.cols());
        hnswlib::InnerProductSpace space(dim);
        hnswlib::HierarchicalNSW<float> index(&space, n, 16, 200);

        #pragma omp parallel for schedule(dynamic, 1024)
        for (size_t i = 0; i < n; ++i) {
            index.addPoint(acts.row(i).data(), i);
        }

        int num_threads = omp_get_max_threads();
        std::vector<ThreadLocalRecords> locals(num_threads);

        #pragma omp parallel for schedule(dynamic, 128)
        for (size_t i = 0; i < n; ++i) {
            auto& tl = locals[omp_get_thread_num()];
            if (token_to_comp.find(vocab[i]) == token_to_comp.end()) continue;
            auto scid = token_to_comp.at(vocab[i]);

            auto result_pq = index.searchKnn(acts.row(i).data(), k_neighbors + 1);

            while (!result_pq.empty()) {
                auto [dist, j] = result_pq.top();
                result_pq.pop();

                if (i == j) continue;
                float sim = 1.0f - dist;
                if (sim < config_.embedding_similarity_threshold) continue;

                if (token_to_comp.find(vocab[j]) == token_to_comp.end()) continue;
                auto tcid = token_to_comp.at(vocab[j]);

                std::vector<uint8_t> rdata = {0x52};
                if (std::memcmp(scid.data(), tcid.data(), 16) < 0) { 
                    rdata.insert(rdata.end(), scid.begin(), scid.end()); 
                    rdata.insert(rdata.end(), tcid.begin(), tcid.end()); 
                } else { 
                    rdata.insert(rdata.end(), tcid.begin(), tcid.end()); 
                    rdata.insert(rdata.end(), scid.begin(), scid.end()); 
                }
                auto rid = BLAKE3Pipeline::hash(rdata);

                if (tl.rel_seen.insert(rid).second && session_rel_seen.find(rid) == session_rel_seen.end()) {
                    std::vector<uint8_t> pdata = {0x50}; pdata.insert(pdata.end(), rid.begin(), rid.end());
                    auto prid = BLAKE3Pipeline::hash(pdata);
                    if (tl.phys_seen.insert(prid).second) {
                        Eigen::Vector4d pos(0.5,0.5,0.5,0.5); pos.normalize();
                        Eigen::Vector4d hc; for (int k=0; k<4; ++k) { hc[k] = (pos[k]+1.0)/2.0; }
                        tl.phys.push_back({prid, HilbertCurve4D::encode(hc), pos, {}});
                    }
                    tl.rel.push_back({rid, prid});
                    for (size_t ord=0; ord<2; ++ord) {
                        auto& cid = (ord==0)?scid:tcid; std::vector<uint8_t> sdata = {0x54};
                        sdata.insert(sdata.end(), rid.begin(), rid.end()); sdata.insert(sdata.end(), cid.begin(), cid.end());
                        uint32_t o = static_cast<uint32_t>(ord); 
                        sdata.insert(sdata.end(), reinterpret_cast<uint8_t*>(&o), reinterpret_cast<uint8_t*>(&o) + 4);
                        tl.rel_seq.push_back({BLAKE3Pipeline::hash(sdata), rid, cid, o, 1});
                    }
                    std::vector<uint8_t> edata; edata.insert(edata.end(), model_id_.begin(), model_id_.end()); edata.insert(edata.end(), rid.begin(), rid.end());
                    uint32_t lidx = static_cast<uint32_t>(layer.layer_index); 
                    edata.insert(edata.end(), reinterpret_cast<uint8_t*>(&lidx), reinterpret_cast<uint8_t*>(&lidx) + 4);
                    edata.push_back(0xFF); tl.ev.push_back({BLAKE3Pipeline::hash(edata), model_id_, rid, true, (double)sim, 1.0});
                    tl.relations_created++;
                }
                tl.rating.push_back({rid, 1, 1000.0 + 1000.0 * (sim - config_.embedding_similarity_threshold)/(1.0-config_.embedding_similarity_threshold), 32.0});
            }
        }
        PhysicalityStore phys_store(db_, true, true); RelationStore rel_store(db_, true, true); RelationSequenceStore rel_seq_store(db_, true, true);
        RelationRatingStore rating_store(db_, true); RelationEvidenceStore ev_store(db_, false, true);
        for (auto& tl : locals) {
            session_rel_seen.insert(tl.rel_seen.begin(), tl.rel_seen.end());
            PostgresConnection::Transaction txn(db_);
            for (auto& r : tl.phys) { phys_store.store(r); } phys_store.flush();
            for (auto& r : tl.rel) { rel_store.store(r); } rel_store.flush();
            for (auto& r : tl.rel_seq) { rel_seq_store.store(r); } rel_seq_store.flush();
            for (auto& r : tl.rating) { rating_store.store(r); } rating_store.flush();
            for (auto& r : tl.ev) { ev_store.store(r); } ev_store.flush();
            txn.commit(); stats.relations_created += tl.relations_created;
        }
        layer_count++;
        std::cout << "      Layer " << layer.layer_index << ": processed" << std::endl;
    }
    std::cout << "    Created " << (stats.relations_created - start_relations) << " FFN relations from " << layer_count << " layers." << std::endl;
}

void ModelIngester::extract_attention_edges(const std::vector<Eigen::MatrixXd>& attention_weights, const std::vector<std::string>& tokens, const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp, ModelIngestionStats& stats) {
    (void)attention_weights; (void)tokens; (void)token_to_comp; (void)stats;
}

void ModelIngester::ingest_tensor(const std::string& name, const TensorData& tensor, ModelIngestionStats& stats) {
    (void)name; (void)tensor; stats.tensors_processed++;
}

std::string ModelIngester::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    char buf[37]; char* p = buf; static const char* hex = "0123456789abcdef";
    for (int i=0; i<16; ++i) {
        if (i==4||i==6||i==8||i==10) *p++ = '-';
        *p++ = hex[(hash[i]>>4)&0xF]; *p++ = hex[hash[i]&0xF];
    }
    *p = '\0'; return std::string(buf, 36);
}

}