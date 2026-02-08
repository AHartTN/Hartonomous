#include <ingestion/model_ingester.hpp>
#include <storage/format_utils.hpp>
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
#include <numeric>
#include <atomic>
#include <chrono>
#include <omp.h>
#include <hnswlib/hnswlib.h>

namespace Hartonomous {


using namespace hartonomous::spatial;
using namespace hartonomous::ml;

using Clock = std::chrono::steady_clock;

static double ms_since(Clock::time_point t0) {
    return std::chrono::duration<double, std::milli>(Clock::now() - t0).count();
}

// Thread-local accumulators for parallel edge extraction
struct ThreadLocalRecords {
    std::vector<PhysicalityRecord> phys;
    std::vector<RelationRecord> rel;
    std::vector<RelationSequenceRecord> rel_seq;
    std::vector<RelationRatingRecord> rating;
    std::vector<RelationEvidenceRecord> ev;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> rel_seen;
    size_t relations_created = 0;
};

// Flush records to DB in a single transaction
static void flush_records(
    PostgresConnection& db,
    std::vector<ThreadLocalRecords>& locals,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    size_t& total_relations)
{
    for (auto& tl : locals)
        session_rel_seen.insert(tl.rel_seen.begin(), tl.rel_seen.end());

    size_t n_created = 0;
    for (auto& tl : locals) n_created += tl.relations_created;
    if (n_created == 0) {
        std::cout << "    (no new relations)" << std::endl;
        return;
    }

    auto t0 = Clock::now();
    PostgresConnection::Transaction txn(db);

    {
        PhysicalityStore store(db, true, true);
        for (auto& tl : locals) for (auto& r : tl.phys) store.store(r);
        store.flush();
    }
    {
        RelationStore store(db, true, true);
        for (auto& tl : locals) for (auto& r : tl.rel) store.store(r);
        store.flush();
    }
    {
        RelationSequenceStore store(db, true, true);
        for (auto& tl : locals) for (auto& r : tl.rel_seq) store.store(r);
        store.flush();
    }
    {
        RelationRatingStore store(db, true);
        for (auto& tl : locals) for (auto& r : tl.rating) store.store(r);
        store.flush();
    }
    {
        RelationEvidenceStore store(db, true, true);
        for (auto& tl : locals) for (auto& r : tl.ev) store.store(r);
        store.flush();
    }

    txn.commit();
    total_relations += n_created;

    std::cout << "    Flushed " << n_created << " relations in "
              << std::fixed << std::setprecision(0) << ms_since(t0) << "ms" << std::endl;
}

// Helper to convert TensorData to Eigen Matrix
static Eigen::MatrixXf tensor_to_matrix(const TensorData* t) {
    if (!t || t->shape.size() != 2) return Eigen::MatrixXf(0, 0);
    size_t rows = t->shape[0];
    size_t cols = t->shape[1];
    Eigen::MatrixXf mat(rows, cols);
    std::memcpy(mat.data(), t->data.data(), rows * cols * sizeof(float));
    return mat;
}

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
    auto t_pipeline = Clock::now();
    try {
        SafetensorLoader loader(package_dir.string());
        auto& metadata = loader.metadata();
        stats.vocab_tokens = metadata.vocab.size();
        std::cout << "Ingesting model: " << metadata.model_name << " (" << stats.vocab_tokens << " tokens)" << std::endl;
        std::cout << "  Content ID: " << hash_to_uuid(model_id_) << std::endl;

        // 1. Provenance
        try {
            PostgresConnection::Transaction txn(db_);
            ContentStore content_store(db_);
            content_store.store({
                model_id_, config_.tenant_id, config_.user_id, 2, model_id_, 0,
                "application/octet-stream", "en", package_dir.string(), "binary"
            });
            content_store.flush();
            txn.commit();
        } catch (...) {}

        // 2. Vocab -> Compositions
        auto t0 = Clock::now();
        auto token_to_comp = ingest_vocab_as_text(metadata.vocab, stats);
        std::cout << "  Phase 1 (vocab): " << std::fixed << std::setprecision(0)
                  << ms_since(t0) << "ms | " << stats.compositions_created << " compositions" << std::endl;

        auto embeddings = loader.get_embeddings();
        if (embeddings.rows() == 0) {
            std::cerr << "  Error: No embeddings found in model. Aborting." << std::endl;
            return stats;
        }
        
        // Normalize embeddings once for all passes
        Eigen::MatrixXf norm_embeddings = embeddings.topRows(std::min(metadata.vocab.size(), (size_t)embeddings.rows()));
        norm_embeddings.rowwise().normalize();

        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> session_rel_seen;

        // 3. Static Embedding Pass (Baseline Similarity)
        auto t1 = Clock::now();
        extract_embedding_edges(metadata.vocab, norm_embeddings, token_to_comp, session_rel_seen, stats);
        std::cout << "  Phase 2 (embedding KNN): " << ms_since(t1) << "ms" << std::endl;

        // 4. Procedural Pass: Attention Mining (Functional Relationships)
        auto attn_layers = loader.get_attention_layers();
        if (!attn_layers.empty()) {
            std::cout << "  Phase 3: Mining Attention layers (Functional paths)..." << std::endl;
            auto t2 = Clock::now();
            for (size_t i = 0; i < attn_layers.size(); i += 4) {
                const auto& layer = attn_layers[i];
                if (!layer.q_weight || !layer.k_weight) continue;

                std::cout << "    Layer " << layer.layer_index << "..." << std::flush;
                auto t_layer = Clock::now();

                Eigen::MatrixXf WQ = tensor_to_matrix(layer.q_weight);
                Eigen::MatrixXf WK = tensor_to_matrix(layer.k_weight);

                Eigen::MatrixXf Q = norm_embeddings * WQ.transpose();
                Eigen::MatrixXf K = norm_embeddings * WK.transpose();
                
                extract_procedural_knn(metadata.vocab, Q, K, token_to_comp, session_rel_seen, stats, 1600.0, "attention");
                std::cout << " (" << ms_since(t_layer) << "ms)" << std::endl;
            }
            std::cout << "  Attention Mining Complete (" << ms_since(t2) << "ms)" << std::endl;
        }

        // 5. Procedural Pass: FFN Mining (Logical Categories)
        auto ffn_layers = loader.get_ffn_layers();
        if (!ffn_layers.empty()) {
            std::cout << "  Phase 4: Mining FFN layers (Logical gates)..." << std::endl;
            auto t3 = Clock::now();
            for (size_t i = 0; i < ffn_layers.size(); i += 8) {
                const auto& layer = ffn_layers[i];
                if (!layer.gate_weight) continue;

                std::cout << "    Layer " << layer.layer_index << "..." << std::flush;
                auto t_layer = Clock::now();

                Eigen::MatrixXf W_gate = tensor_to_matrix(layer.gate_weight);
                Eigen::MatrixXf G = norm_embeddings * W_gate.transpose();
                G = G.array() / (1.0f + (-G.array()).exp());

                extract_procedural_knn(metadata.vocab, G, G, token_to_comp, session_rel_seen, stats, 1800.0, "ffn_logic");
                std::cout << " (" << ms_since(t_layer) << "ms)" << std::endl;
            }
            std::cout << "  FFN Mining Complete (" << ms_since(t3) << "ms)" << std::endl;
        }

        double total_ms = ms_since(t_pipeline);
        std::cout << "\n  === Substrate Reinforcement Complete ===" << std::endl;
        std::cout << "  Total time:    " << (total_ms / 1000.0) << "s" << std::endl;
        std::cout << "  Relations:     " << stats.relations_created << std::endl;
        std::cout << "  Throughput:    " << (total_ms > 0 ? (stats.relations_created / (total_ms / 1000.0)) : 0) << " relations/sec" << std::endl;

    } catch (const std::exception& e) {
        std::cerr << "Model ingestion failed: " << e.what() << std::endl;
    }
    return stats;
}

std::unordered_map<std::string, BLAKE3Pipeline::Hash>
ModelIngester::ingest_vocab_as_text(const std::vector<std::string>& vocab, ModelIngestionStats& stats) {
    std::cout << "    Ingesting " << vocab.size() << " vocab tokens as compositions..." << std::endl;
    std::unordered_map<std::string, BLAKE3Pipeline::Hash> token_to_comp;
    token_to_comp.reserve(vocab.size());

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
    struct TokenMapping {
        std::string token;
        BLAKE3Pipeline::Hash second;
        Eigen::Vector4d centroid;
    };
    struct Local {
        std::vector<PhysicalityRecord> phys;
        std::vector<CompositionRecord> comp;
        std::vector<CompositionSequenceRecord> seq;
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;
        std::vector<TokenMapping> mappings;
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
        for (const auto& aid : atom_ids) cdata.insert(cdata.end(), aid.begin(), aid.end());
        auto cid = BLAKE3Pipeline::hash(cdata);

        Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
        for (const auto& p : positions) centroid += p;
        centroid /= static_cast<double>(positions.size());
        double nrm = centroid.norm();
        if (nrm > 1e-10) centroid /= nrm; else centroid = Eigen::Vector4d(1, 0, 0, 0);

        tl.mappings.push_back({token, cid, centroid});

        std::vector<uint8_t> pdata = {0x50};
        pdata.insert(pdata.end(), reinterpret_cast<const uint8_t*>(centroid.data()),
                     reinterpret_cast<const uint8_t*>(centroid.data()) + sizeof(double) * 4);
        for (const auto& p : positions)
            pdata.insert(pdata.end(), reinterpret_cast<const uint8_t*>(p.data()),
                         reinterpret_cast<const uint8_t*>(p.data()) + sizeof(double) * 4);
        auto pid = BLAKE3Pipeline::hash(pdata);

        if (tl.phys_seen.insert(pid).second) {
            Eigen::Vector4d hc;
            for (int k = 0; k < 4; ++k) hc[k] = (centroid[k] + 1.0) / 2.0;
            tl.phys.push_back({pid, HilbertCurve4D::encode(hc, HilbertCurve4D::EntityType::Composition), centroid, positions});
        }
        tl.comp.push_back({cid, pid});
        tl.created++;

        for (size_t k = 0; k < atom_ids.size(); ) {
            uint32_t ord = static_cast<uint32_t>(k);
            uint32_t occ = 1;
            while (k + occ < atom_ids.size() && atom_ids[k + occ] == atom_ids[k]) ++occ;
            std::vector<uint8_t> sdata = {0x53};
            sdata.insert(sdata.end(), cid.begin(), cid.end());
            sdata.insert(sdata.end(), atom_ids[k].begin(), atom_ids[k].end());
            sdata.insert(sdata.end(), reinterpret_cast<uint8_t*>(&ord), reinterpret_cast<uint8_t*>(&ord) + 4);
            tl.seq.push_back({BLAKE3Pipeline::hash(sdata), cid, atom_ids[k], ord, occ});
            k += occ;
        }
    }

    for (auto& tl : locals) {
        for (auto& m : tl.mappings) {
            token_to_comp[m.token] = m.second;
            comp_centroids_[m.second] = m.centroid;
        }
        stats.compositions_created += tl.created;
    }

    PostgresConnection::Transaction txn(db_);
    {
        PhysicalityStore store(db_, true, true);
        for (auto& tl : locals) for (auto& r : tl.phys) store.store(r);
        store.flush();
    }
    {
        CompositionStore store(db_, true, true);
        for (auto& tl : locals) for (auto& r : tl.comp) store.store(r);
        store.flush();
    }
    {
        CompositionSequenceStore store(db_, true, true);
        for (auto& tl : locals) for (auto& r : tl.seq) store.store(r);
        store.flush();
    }
    txn.commit();

    return token_to_comp;
}

void ModelIngester::extract_embedding_edges(
    const std::vector<std::string>& vocab, const Eigen::MatrixXf& norm_embeddings,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = static_cast<size_t>(norm_embeddings.rows());
    if (n < 2) return;
    int dim = static_cast<int>(norm_embeddings.cols());
    size_t k = std::min(config_.max_neighbors_per_token, n - 1);
    float threshold = static_cast<float>(config_.embedding_similarity_threshold);

    std::cout << "    Building HNSW index for " << n << " tokens (dim=" << dim << ")..." << std::flush;
    auto t_start = Clock::now();

    hnswlib::InnerProductSpace space(dim);
    hnswlib::HierarchicalNSW<float>* alg_hnsw = new hnswlib::HierarchicalNSW<float>(&space, n, 16, 200);

    #pragma omp parallel for schedule(dynamic, 1024)
    for (size_t i = 0; i < n; ++i) {
        alg_hnsw->addPoint(norm_embeddings.row(i).data(), i);
    }
    std::cout << " (" << ms_since(t_start) << "ms)" << std::endl;

    std::cout << "    Extracting relations (k=" << k << ", threshold=" << threshold << ")..." << std::flush;
    t_start = Clock::now();

    double base_elo = 1000.0;
    double elo_range = 500.0;
    std::atomic<size_t> edges_found{0};
    int num_threads = omp_get_max_threads();
    std::vector<ThreadLocalRecords> locals(num_threads);

    #pragma omp parallel for schedule(dynamic, 512)
    for (size_t i = 0; i < n; ++i) {
        auto& tl = locals[omp_get_thread_num()];
        auto it_s = token_to_comp.find(vocab[i]);
        if (it_s == token_to_comp.end()) continue;
        const auto& scid = it_s->second;

        auto result = alg_hnsw->searchKnn(norm_embeddings.row(i).data(), k + 1);
        
        while (!result.empty()) {
            auto top = result.top();
            result.pop();
            if (top.second == i) continue;

            float sim = 1.0f - top.first;
            if (sim < threshold) continue;

            auto it_t = token_to_comp.find(vocab[top.second]);
            if (it_t == token_to_comp.end()) continue;
            const auto& tcid = it_t->second;

            const auto& lo = (std::memcmp(scid.data(), tcid.data(), 16) < 0) ? scid : tcid;
            const auto& hi = (&lo == &scid) ? tcid : scid;

            uint8_t rdata[33];
            rdata[0] = 0x52;
            std::memcpy(rdata + 1, lo.data(), 16);
            std::memcpy(rdata + 17, hi.data(), 16);
            auto rid = BLAKE3Pipeline::hash(rdata, 33);

            if (tl.rel_seen.insert(rid).second && session_rel_seen.find(rid) == session_rel_seen.end()) {
                Eigen::Vector4d rel_centroid;
                auto it_sc = comp_centroids_.find(scid);
                auto it_tc = comp_centroids_.find(tcid);
                
                if (it_sc != comp_centroids_.end() && it_tc != comp_centroids_.end()) {
                    rel_centroid = (it_sc->second + it_tc->second) * 0.5;
                    double nrm = rel_centroid.norm();
                    if (nrm > 1e-10) rel_centroid /= nrm;
                    else rel_centroid = Eigen::Vector4d(1, 0, 0, 0);
                } else {
                    rel_centroid = Eigen::Vector4d(1, 0, 0, 0);
                }

                std::vector<Eigen::Vector4d> traj;
                if (it_sc != comp_centroids_.end()) traj.push_back(it_sc->second);
                if (it_tc != comp_centroids_.end()) traj.push_back(it_tc->second);

                size_t rel_phys_data_len = 1 + sizeof(double) * 4 + traj.size() * sizeof(double) * 4;
                std::vector<uint8_t> pdata(rel_phys_data_len);
                pdata[0] = 0x50;
                std::memcpy(pdata.data() + 1, rel_centroid.data(), sizeof(double) * 4);
                for (size_t pt_idx = 0; pt_idx < traj.size(); ++pt_idx)
                    std::memcpy(pdata.data() + 1 + sizeof(double) * 4 + pt_idx * sizeof(double) * 4, traj[pt_idx].data(), sizeof(double) * 4);
                auto prid = BLAKE3Pipeline::hash(pdata.data(), rel_phys_data_len);

                if (tl.phys_seen.insert(prid).second) {
                    Eigen::Vector4d hc;
                    for (int d = 0; d < 4; ++d) hc[d] = (rel_centroid[d] + 1.0) / 2.0;
                    tl.phys.push_back({prid, HilbertCurve4D::encode(hc, HilbertCurve4D::EntityType::Relation), rel_centroid, traj});
                }
                tl.rel.push_back({rid, prid});

                for (uint32_t ord = 0; ord < 2; ++ord) {
                    const auto& cid = (ord == 0) ? scid : tcid;
                    uint8_t sdata[37];
                    sdata[0] = 0x54;
                    std::memcpy(sdata + 1, rid.data(), 16);
                    std::memcpy(sdata + 17, cid.data(), 16);
                    std::memcpy(sdata + 33, &ord, 4);
                    tl.rel_seq.push_back({BLAKE3Pipeline::hash(sdata, 37), rid, cid, ord, 1});
                }

                double clamped_sim = std::clamp(static_cast<double>(sim), 0.0, 1.0);
                uint8_t edata[32];
                std::memcpy(edata, model_id_.data(), 16);
                std::memcpy(edata + 16, rid.data(), 16);
                tl.ev.push_back({BLAKE3Pipeline::hash(edata, 32), model_id_, rid, true,
                                 base_elo + elo_range * clamped_sim, clamped_sim});
                tl.relations_created++;
                edges_found.fetch_add(1, std::memory_order_relaxed);
            }
            tl.rating.push_back({rid, 1, base_elo + elo_range * static_cast<double>(sim), 32.0});
        }
    }

    delete alg_hnsw;
    std::cout << " (" << ms_since(t_start) << "ms) | Edges: " << edges_found.load() << std::endl;
    flush_records(db_, locals, session_rel_seen, stats.relations_created);
}

void ModelIngester::extract_procedural_knn(
    const std::vector<std::string>& vocab, const Eigen::MatrixXf& Q, const Eigen::MatrixXf& K,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats, double base_elo, const std::string& type_tag) {

    size_t n = static_cast<size_t>(Q.rows());
    int dim = static_cast<int>(Q.cols());
    size_t k = 16; 
    float threshold = 0.5f;

    hnswlib::InnerProductSpace space(dim);
    hnswlib::HierarchicalNSW<float>* alg_hnsw = new hnswlib::HierarchicalNSW<float>(&space, n, 16, 200);

    #pragma omp parallel for schedule(dynamic, 1024)
    for (size_t i = 0; i < n; ++i) {
        Eigen::VectorXf row = K.row(i);
        row.normalize();
        alg_hnsw->addPoint(row.data(), i);
    }

    int num_threads = omp_get_max_threads();
    std::vector<ThreadLocalRecords> locals(num_threads);

    #pragma omp parallel for schedule(dynamic, 512)
    for (size_t i = 0; i < n; ++i) {
        auto& tl = locals[omp_get_thread_num()];
        auto it_s = token_to_comp.find(vocab[i]);
        if (it_s == token_to_comp.end()) continue;
        const auto& scid = it_s->second;

        Eigen::VectorXf q_vec = Q.row(i);
        q_vec.normalize();
        auto result = alg_hnsw->searchKnn(q_vec.data(), k + 1);
        
        while (!result.empty()) {
            auto top = result.top();
            result.pop();
            if (top.second == i) continue;

            float sim = 1.0f - top.first;
            if (sim < threshold) continue;

            auto it_t = token_to_comp.find(vocab[top.second]);
            if (it_t == token_to_comp.end()) continue;
            const auto& tcid = it_t->second;

            const auto& lo = (std::memcmp(scid.data(), tcid.data(), 16) < 0) ? scid : tcid;
            const auto& hi = (&lo == &scid) ? tcid : scid;

            uint8_t rdata[33];
            rdata[0] = 0x52;
            std::memcpy(rdata + 1, lo.data(), 16);
            std::memcpy(rdata + 17, hi.data(), 16);
            auto rid = BLAKE3Pipeline::hash(rdata, 33);

            if (tl.rel_seen.insert(rid).second && session_rel_seen.find(rid) == session_rel_seen.end()) {
                Eigen::Vector4d rel_centroid;
                auto it_sc = comp_centroids_.find(scid);
                auto it_tc = comp_centroids_.find(tcid);
                if (it_sc != comp_centroids_.end() && it_tc != comp_centroids_.end()) {
                    rel_centroid = (it_sc->second + it_tc->second) * 0.5;
                    double nrm = rel_centroid.norm();
                    if (nrm > 1e-10) rel_centroid /= nrm; else rel_centroid = Eigen::Vector4d(1, 0, 0, 0);
                } else {
                    rel_centroid = Eigen::Vector4d(1, 0, 0, 0);
                }

                std::vector<Eigen::Vector4d> traj;
                if (it_sc != comp_centroids_.end()) traj.push_back(it_sc->second);
                if (it_tc != comp_centroids_.end()) traj.push_back(it_tc->second);

                size_t rel_phys_data_len = 1 + sizeof(double) * 4 + traj.size() * sizeof(double) * 4;
                std::vector<uint8_t> pdata(rel_phys_data_len);
                pdata[0] = 0x50;
                std::memcpy(pdata.data() + 1, rel_centroid.data(), sizeof(double) * 4);
                for (size_t pt_idx = 0; pt_idx < traj.size(); ++pt_idx)
                    std::memcpy(pdata.data() + 1 + sizeof(double) * 4 + pt_idx * sizeof(double) * 4, traj[pt_idx].data(), sizeof(double) * 4);
                auto prid = BLAKE3Pipeline::hash(pdata.data(), rel_phys_data_len);

                if (tl.phys_seen.insert(prid).second) {
                    Eigen::Vector4d hc;
                    for (int d = 0; d < 4; ++d) hc[d] = (rel_centroid[d] + 1.0) / 2.0;
                    tl.phys.push_back({prid, HilbertCurve4D::encode(hc, HilbertCurve4D::EntityType::Relation), rel_centroid, traj});
                }
                tl.rel.push_back({rid, prid});

                for (uint32_t ord = 0; ord < 2; ++ord) {
                    const auto& cid = (ord == 0) ? scid : tcid;
                    uint8_t sdata[37]; sdata[0] = 0x54;
                    std::memcpy(sdata + 1, rid.data(), 16);
                    std::memcpy(sdata + 17, cid.data(), 16);
                    std::memcpy(sdata + 33, &ord, 4);
                    tl.rel_seq.push_back({BLAKE3Pipeline::hash(sdata, 37), rid, cid, ord, 1});
                }

                double clamped_sim = std::clamp(static_cast<double>(sim), 0.0, 1.0);
                uint8_t edata[32];
                std::memcpy(edata, model_id_.data(), 16);
                std::memcpy(edata + 16, rid.data(), 16);
                tl.ev.push_back({BLAKE3Pipeline::hash(edata, 32), model_id_, rid, true,
                                 base_elo + 200.0 * clamped_sim, clamped_sim});
                tl.relations_created++;
            }
            tl.rating.push_back({rid, 1, base_elo + 200.0 * static_cast<double>(sim), 32.0});
        }
    }

    delete alg_hnsw;
    flush_records(db_, locals, session_rel_seen, stats.relations_created);
}

}
