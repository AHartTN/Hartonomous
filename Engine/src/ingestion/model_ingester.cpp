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
#include <numeric>
#include <atomic>
#include <queue>
#include <chrono>
#include <omp.h>

namespace Hartonomous {

using namespace hartonomous::spatial;
using namespace hartonomous::ml;
using Clock = std::chrono::steady_clock;

static double ms_since(Clock::time_point t0) {
    return std::chrono::duration<double, std::milli>(Clock::now() - t0).count();
}

// Thread-local accumulators for parallel KNN edge extraction
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

// Flush one layer's worth of records to DB in a single transaction
static void flush_layer_records(
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
        std::cout << "      (no new relations)" << std::endl;
        return;
    }

    auto t0 = Clock::now();

    // All stores in one transaction — each COPY completes sequentially via flush()
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

    std::cout << "      Flushed " << n_created << " relations in " 
              << std::fixed << std::setprecision(0) << ms_since(t0) << "ms" << std::endl;
}

// Score a layer's representation quality: mean max similarity
// Low score = collapsed or scattered = skip it
static float score_layer_quality(const Eigen::MatrixXf& vecs, size_t sample_size = 512) {
    size_t n = static_cast<size_t>(vecs.rows());
    if (n < 10) return 0.0f;
    
    size_t step = std::max(size_t(1), n / sample_size);
    double total_max_sim = 0.0;
    size_t count = 0;
    
    for (size_t i = 0; i < n; i += step) {
        float best = -1.0f;
        // Compare against a random sample of other tokens
        for (size_t s = 0; s < std::min(sample_size, n); ++s) {
            size_t j = (i + 1 + s * step) % n;
            if (j == i) continue;
            float sim = vecs.row(i).dot(vecs.row(j));
            if (sim > best) best = sim;
        }
        if (best > -1.0f) { total_max_sim += best; count++; }
    }
    return count > 0 ? static_cast<float>(total_max_sim / count) : 0.0f;
}

// Blocked GEMM edge extraction
// Instead of 30K individual GEMV calls, compute Q[block]*K^T as a single MKL GEMM.
// Block size 1024: 1024×30K×4B = 120MB per block, ~30 blocks, MKL-optimized.
// Scan result for above-threshold entries (the intersections).
static void extract_streaming_edges(
    const Eigen::MatrixXf& query_vecs,
    const Eigen::MatrixXf& key_vecs,
    size_t n, size_t max_k, float sim_threshold,
    const std::vector<std::string>& vocab,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    const std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    const BLAKE3Pipeline::Hash& model_id,
    const std::vector<uint8_t>& evidence_suffix,
    double base_elo, double elo_range,
    const std::unordered_map<BLAKE3Pipeline::Hash, Eigen::Vector4d, HashHasher>& comp_centroids,
    std::vector<ThreadLocalRecords>& locals)
{
    auto t0 = Clock::now();
    constexpr size_t BLOCK = 1024;

    std::atomic<size_t> total_candidates{0};
    std::atomic<size_t> total_above_threshold{0};

    for (size_t block_start = 0; block_start < n; block_start += BLOCK) {
        size_t block_end = std::min(block_start + BLOCK, n);
        size_t block_rows = block_end - block_start;

        // Single MKL GEMM: (block_rows, dim) × (dim, n) → (block_rows, n)
        Eigen::MatrixXf scores = query_vecs.middleRows(block_start, block_rows) * key_vecs.topRows(n).transpose();

        // Parallel scan for above-threshold entries
        #pragma omp parallel for schedule(dynamic, 32)
        for (size_t bi = 0; bi < block_rows; ++bi) {
            size_t i = block_start + bi;
            auto& tl = locals[omp_get_thread_num()];
            auto it_s = token_to_comp.find(vocab[i]);
            if (it_s == token_to_comp.end()) continue;
            const auto& scid = it_s->second;

            // Collect above-threshold entries from this row
            struct Edge { size_t j; float sim; };
            std::vector<Edge> candidates;
            candidates.reserve(128);

            for (size_t j = 0; j < n; ++j) {
                if (j == i) continue;
                float sim = scores(bi, j);
                if (sim < sim_threshold) continue;
                candidates.push_back({j, sim});
            }

            if (candidates.empty()) continue;
            total_above_threshold.fetch_add(candidates.size(), std::memory_order_relaxed);

            // Keep top-K
            if (candidates.size() > max_k) {
                std::partial_sort(candidates.begin(), candidates.begin() + max_k, candidates.end(),
                                  [](const Edge& a, const Edge& b) { return a.sim > b.sim; });
                candidates.resize(max_k);
            }
            total_candidates.fetch_add(candidates.size(), std::memory_order_relaxed);

            for (auto& [j, sim] : candidates) {
                auto it_t = token_to_comp.find(vocab[j]);
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
                    auto it_sc = comp_centroids.find(scid);
                    auto it_tc = comp_centroids.find(tcid);
                    if (it_sc != comp_centroids.end() && it_tc != comp_centroids.end()) {
                        rel_centroid = (it_sc->second + it_tc->second) * 0.5;
                        double nrm = rel_centroid.norm();
                        if (nrm > 1e-10) rel_centroid /= nrm;
                        else rel_centroid = Eigen::Vector4d(1, 0, 0, 0);
                    } else {
                        rel_centroid = Eigen::Vector4d(0.5, 0.5, 0.5, 0.5);
                        rel_centroid.normalize();
                    }

                    uint8_t pdata[33];
                    pdata[0] = 0x50;
                    std::memcpy(pdata + 1, rel_centroid.data(), sizeof(double) * 4);
                    auto prid = BLAKE3Pipeline::hash(pdata, 33);

                    if (tl.phys_seen.insert(prid).second) {
                        Eigen::Vector4d hc;
                        for (int d = 0; d < 4; ++d) hc[d] = (rel_centroid[d] + 1.0) / 2.0;
                        std::vector<Eigen::Vector4d> traj;
                        if (it_sc != comp_centroids.end()) traj.push_back(it_sc->second);
                        if (it_tc != comp_centroids.end()) traj.push_back(it_tc->second);
                        tl.phys.push_back({prid, HilbertCurve4D::encode(hc), rel_centroid, traj});
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

                    size_t ev_len = 32 + evidence_suffix.size();
                    uint8_t edata[64];
                    std::memcpy(edata, model_id.data(), 16);
                    std::memcpy(edata + 16, rid.data(), 16);
                    if (!evidence_suffix.empty())
                        std::memcpy(edata + 32, evidence_suffix.data(), evidence_suffix.size());
                    tl.ev.push_back({BLAKE3Pipeline::hash(edata, ev_len), model_id, rid, true, static_cast<double>(sim), 1.0});
                    tl.relations_created++;
                }

                tl.rating.push_back({rid, 1, base_elo + elo_range * static_cast<double>(sim), 32.0});
            }
        }
    }

    double total_ms = ms_since(t0);
    size_t total_new = 0;
    for (auto& tl : locals) total_new += tl.relations_created;
    std::cout << "      Blocked GEMM (" << BLOCK << "): " << std::fixed << std::setprecision(0) << total_ms
              << "ms | " << total_new << " new relations"
              << " (above-threshold: " << total_above_threshold.load()
              << ", kept: " << total_candidates.load() << ")" << std::endl;
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
        std::cout << "Ingesting model with " << stats.vocab_tokens << " vocab tokens..." << std::endl;
        std::cout << "  Content ID: " << hash_to_uuid(model_id_) << std::endl;

        // Bulk COPY through temp tables with ON CONFLICT handles all dedup/upsert
        std::cout << "  Starting model ingestion pipeline..." << std::endl;

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

        // Phase 1: Vocab → Compositions
        auto t0 = Clock::now();
        auto token_to_comp = ingest_vocab_as_text(metadata.vocab, stats);
        std::cout << "  Phase 1 (vocab): " << std::fixed << std::setprecision(0) 
                  << ms_since(t0) << "ms | " << stats.compositions_created << " compositions" << std::endl;

        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> session_rel_seen;

        // Phase 2: Embedding layer → relations (baseline for semantic deltas)
        auto embeddings = loader.get_embeddings();
        Eigen::MatrixXf norm_embeddings;
        if (embeddings.rows() > 0) {
            auto t1 = Clock::now();
            size_t n = std::min(metadata.vocab.size(), static_cast<size_t>(embeddings.rows()));
            norm_embeddings = embeddings.topRows(n);
            norm_embeddings.rowwise().normalize();
            extract_embedding_edges(metadata.vocab, norm_embeddings, token_to_comp, session_rel_seen, stats);
            std::cout << "  Phase 2 (embeddings): " << ms_since(t1) << "ms" << std::endl;
        }

        // Phase 3: Attention layers → relations (with quality scoring + semantic deltas)
        auto attention_layers = loader.get_attention_layers();
        if (!attention_layers.empty() && norm_embeddings.rows() > 0) {
            auto t2 = Clock::now();
            extract_attention_layer_edges(attention_layers, metadata.vocab, embeddings,
                                          norm_embeddings, token_to_comp, session_rel_seen, stats);
            std::cout << "  Phase 3 (attention): " << ms_since(t2) << "ms" << std::endl;
        }

        // Phase 4: FFN layers → relations (with quality scoring + semantic deltas)
        auto ffn_layers = loader.get_ffn_layers();
        if (!ffn_layers.empty() && norm_embeddings.rows() > 0) {
            auto t3 = Clock::now();
            extract_ffn_layer_edges(ffn_layers, metadata.vocab, embeddings,
                                     norm_embeddings, token_to_comp, session_rel_seen, stats);
            std::cout << "  Phase 4 (FFN): " << ms_since(t3) << "ms" << std::endl;
        }

        double total_ms = ms_since(t_pipeline);
        double rel_per_sec = (total_ms > 0) ? (stats.relations_created / (total_ms / 1000.0)) : 0;
        std::cout << "\n  === Model Ingestion Complete ===" << std::endl;
        std::cout << "  Total time:    " << std::fixed << std::setprecision(1) << (total_ms / 1000.0) << "s" << std::endl;
        std::cout << "  Compositions:  " << stats.compositions_created << std::endl;
        std::cout << "  Relations:     " << stats.relations_created << std::endl;
        std::cout << "  Throughput:    " << std::setprecision(0) << rel_per_sec << " relations/sec" << std::endl;

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
        BLAKE3Pipeline::Hash second; // composition ID (named to minimize other changes)
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
                     reinterpret_cast<const uint8_t*>(centroid.data()) + 32);
        auto pid = BLAKE3Pipeline::hash(pdata);

        if (tl.phys_seen.insert(pid).second) {
            Eigen::Vector4d hc;
            for (int k = 0; k < 4; ++k) hc[k] = (centroid[k] + 1.0) / 2.0;
            tl.phys.push_back({pid, HilbertCurve4D::encode(hc), centroid, positions});
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

    // Single transaction for all vocab stores
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
    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);
    std::cout << "    Embedding layer: " << n << " tokens, k=" << k_neighbors 
              << ", threshold=" << std::fixed << std::setprecision(2) 
              << config_.embedding_similarity_threshold << std::endl;

    float quality = score_layer_quality(norm_embeddings);
    std::cout << "      Layer quality score: " << std::fixed << std::setprecision(3) << quality << std::endl;

    int num_threads = omp_get_max_threads();
    std::vector<ThreadLocalRecords> locals(num_threads);

    // Embedding layer: proximity relations (nearest neighbors in embedding space)
    extract_streaming_edges(norm_embeddings, norm_embeddings, n, k_neighbors,
                      config_.embedding_similarity_threshold,
                      vocab, token_to_comp, session_rel_seen, model_id_,
                      {}, 800.0, 1200.0, comp_centroids_, locals);

    flush_layer_records(db_, locals, session_rel_seen, stats.relations_created);
}

void ModelIngester::extract_attention_layer_edges(
    const std::vector<AttentionLayer>& layers, const std::vector<std::string>& vocab,
    const Eigen::MatrixXf& embeddings, const Eigen::MatrixXf& norm_embeddings,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2 || layers.empty()) return;
    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);

    std::cout << "    Attention: " << layers.size() << " layers, " << n << " tokens, k=" << k_neighbors << std::endl;

    for (const auto& layer : layers) {
        if (!layer.q_weight || !layer.k_weight) continue;
        auto t0 = Clock::now();

        Eigen::Map<const Eigen::MatrixXf> Q(layer.q_weight->data.data(), layer.q_weight->shape[0], layer.q_weight->shape[1]);
        Eigen::Map<const Eigen::MatrixXf> K(layer.k_weight->data.data(), layer.k_weight->shape[0], layer.k_weight->shape[1]);

        // Project embeddings through Q and K — HNSW on these IS the attention top-K
        Eigen::MatrixXf Qp = embeddings.topRows(n) * Q.transpose();
        Qp.rowwise().normalize();
        Eigen::MatrixXf Kp = embeddings.topRows(n) * K.transpose();
        Kp.rowwise().normalize();

        float quality = score_layer_quality(Qp);
        std::cout << "      Attention layer " << layer.layer_index 
                  << " quality=" << std::fixed << std::setprecision(3) << quality;
        if (quality < 0.05f) {
            std::cout << " → SKIPPED (collapsed)" << std::endl;
            continue;
        }
        std::cout << std::endl;

        int num_threads = omp_get_max_threads();
        std::vector<ThreadLocalRecords> locals(num_threads);

        uint32_t lidx = static_cast<uint32_t>(layer.layer_index);
        std::vector<uint8_t> ev_suffix(reinterpret_cast<uint8_t*>(&lidx), reinterpret_cast<uint8_t*>(&lidx) + 4);

        float threshold = config_.embedding_similarity_threshold;
        double elo_range = 1000.0 / (1.0 - threshold);
        double base_elo = 1000.0 - elo_range * threshold;

        // Attention: relevance relations (Q·K^T = semantic intersections)
        // Threshold discards near-zero scores. No neighborhood filter — relevance ≠ proximity.
        extract_streaming_edges(Qp, Kp, n, k_neighbors, threshold,
                          vocab, token_to_comp, session_rel_seen, model_id_,
                          ev_suffix, base_elo, elo_range, comp_centroids_, locals);

        std::cout << "      Attention layer " << layer.layer_index << " (" 
                  << std::fixed << std::setprecision(0) << ms_since(t0) << "ms total): ";
        flush_layer_records(db_, locals, session_rel_seen, stats.relations_created);
    }
}

void ModelIngester::extract_ffn_layer_edges(
    const std::vector<FFNLayer>& layers, const std::vector<std::string>& vocab,
    const Eigen::MatrixXf& embeddings, const Eigen::MatrixXf& norm_embeddings,
    const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
    ModelIngestionStats& stats) {

    size_t n = std::min(vocab.size(), static_cast<size_t>(embeddings.rows()));
    if (n < 2 || layers.empty()) return;
    size_t k_neighbors = std::min(config_.max_neighbors_per_token, n - 1);

    std::cout << "    FFN: " << layers.size() << " layers, " << n << " tokens, k=" << k_neighbors << std::endl;

    for (const auto& layer : layers) {
        const TensorData* p_tensor = layer.gate_weight ? layer.gate_weight : layer.up_weight;
        if (!p_tensor || p_tensor->data.empty()) continue;
        auto t0 = Clock::now();

        Eigen::Map<const Eigen::MatrixXf> W(p_tensor->data.data(), p_tensor->shape[0], p_tensor->shape[1]);
        Eigen::MatrixXf acts = (embeddings.topRows(n) * W.transpose()).cwiseMax(0);
        acts.rowwise().normalize();

        float quality = score_layer_quality(acts);
        std::cout << "      FFN layer " << layer.layer_index
                  << " quality=" << std::fixed << std::setprecision(3) << quality;
        if (quality < 0.10f) {
            std::cout << " → SKIPPED (noise)" << std::endl;
            continue;
        }
        std::cout << std::endl;

        int num_threads = omp_get_max_threads();
        std::vector<ThreadLocalRecords> locals(num_threads);

        uint32_t lidx = static_cast<uint32_t>(layer.layer_index);
        std::vector<uint8_t> ev_suffix(reinterpret_cast<uint8_t*>(&lidx), reinterpret_cast<uint8_t*>(&lidx) + 4);
        ev_suffix.push_back(0xFF);

        // FFN activations are non-negative (ReLU) + normalized → inherently high similarity.
        // Use a higher threshold than embeddings to avoid explosion.
        float threshold = std::max(config_.embedding_similarity_threshold, 0.70f);
        double elo_range = 1000.0 / (1.0 - threshold);
        double base_elo = 1000.0 - elo_range * threshold;

        // FFN: relevance relations (activation intersections)
        extract_streaming_edges(acts, acts, n, k_neighbors, threshold,
                          vocab, token_to_comp, session_rel_seen, model_id_,
                          ev_suffix, base_elo, elo_range, comp_centroids_, locals);

        std::cout << "      FFN layer " << layer.layer_index << " (" 
                  << std::fixed << std::setprecision(0) << ms_since(t0) << "ms total): ";
        flush_layer_records(db_, locals, session_rel_seen, stats.relations_created);
    }
}

void ModelIngester::extract_attention_edges(const std::vector<Eigen::MatrixXd>&, const std::vector<std::string>&, const std::unordered_map<std::string, BLAKE3Pipeline::Hash>&, ModelIngestionStats&) {}

void ModelIngester::ingest_tensor(const std::string&, const TensorData&, ModelIngestionStats& stats) { stats.tensors_processed++; }

std::string ModelIngester::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    char buf[37]; char* p = buf; static const char* hex = "0123456789abcdef";
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) *p++ = '-';
        *p++ = hex[(hash[i] >> 4) & 0xF]; *p++ = hex[hash[i] & 0xF];
    }
    *p = '\0'; return std::string(buf, 36);
}

}
