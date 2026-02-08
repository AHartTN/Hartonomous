#pragma once

#include <Eigen/Core>
#include <Eigen/Sparse>
#include <unsupported/Eigen/CXX11/Tensor>
#include <cstdint>
#include <vector>
#include <unordered_map>
#include <string>
#include <cmath>
#include <algorithm>

#include <database/postgres_connection.hpp>
#include <storage/physicality_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <storage/composition_store.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <hashing/blake3_pipeline.hpp>

namespace hartonomous::ml {

/**
 * @brief AI Model Extraction: Convert ANY model to Hartonomous semantic edges
 *
 * This module extracts semantic relationships from different AI architectures
 * and converts them to the universal Hartonomous representation:
 *   - Nodes (tokens/features) → Atoms/Compositions
 *   - Edges (connections) → Semantic edges with ELO ratings
 *
 * Supported Architectures:
 *   1. Transformers (attention-based): Direct mapping from attention weights to ELO
 *   2. CNNs (convolutional): Filter responses → spatial semantic edges
 *   3. RNNs/LSTMs: Temporal connections → sequential edges
 *   4. Graph Neural Networks: Already graph-based, direct conversion
 *
 * CRITICAL INSIGHT from user:
 *   "Attention is easy... input token A's weight/intensity/beaten path to output
 *    token B... that's an ELO match right there"
 */

/**
 * @brief Semantic edge extracted from AI model
 */
struct SemanticEdge {
    uint64_t source_id = 0;
    uint64_t target_id = 0;
    std::string source_token;
    std::string target_token;
    double weight = 0.0;
    std::string edge_type;
    int32_t layer_index = -1;
    int32_t head_index = -1;

    /**
     * @brief Convert weight to ELO rating
     * Formula: ELO = 1500 + 500 * (2 * weight - 1)
     */
    int32_t to_elo() const {
        return static_cast<int32_t>(1500.0 + 500.0 * (2.0 * weight - 1.0));
    }
};

/**
 * @brief Extracted model graph
 */
struct ExtractedGraph {
    std::vector<SemanticEdge> edges;
    std::unordered_map<uint64_t, std::string> node_labels;
    std::string model_name;
    std::string architecture_type;
    int32_t num_layers = 0;
};

// ==============================================================================
// 1. TRANSFORMER ATTENTION → ELO EDGES
// ==============================================================================

class TransformerExtractor {
public:
    static ExtractedGraph extract(
        const std::vector<Eigen::MatrixXd>& attention_weights,
        const std::vector<uint64_t>& tokens,
        double sparsity_threshold = 0.01
    ) {
        ExtractedGraph graph;
        graph.architecture_type = "Transformer";
        graph.num_layers = static_cast<int32_t>(attention_weights.size());

        for (size_t layer_head_idx = 0; layer_head_idx < attention_weights.size(); ++layer_head_idx) {
            const Eigen::MatrixXd& attn = attention_weights[layer_head_idx];
            int32_t layer = static_cast<int32_t>(layer_head_idx / 8);
            int32_t head = static_cast<int32_t>(layer_head_idx % 8);

            for (int i = 0; i < attn.rows(); ++i) {
                for (int j = 0; j < attn.cols(); ++j) {
                    double weight = attn(i, j);
                    if (weight < sparsity_threshold) continue;

                    SemanticEdge edge;
                    edge.source_id = tokens[i];
                    edge.target_id = tokens[j];
                    edge.weight = weight;
                    edge.edge_type = "attention";
                    edge.layer_index = layer;
                    edge.head_index = head;
                    graph.edges.push_back(edge);
                }
            }
        }
        return graph;
    }
};

// ==============================================================================
// 2. CNN FILTERS → SPATIAL SEMANTIC EDGES
// ==============================================================================

class CNNExtractor {
public:
    static ExtractedGraph extract(
        const Eigen::Tensor<float, 4>& filters,
        int layer_idx,
        double threshold = 0.1
    ) {
        ExtractedGraph graph;
        graph.architecture_type = "CNN";

        int out_channels = static_cast<int>(filters.dimension(0));
        int in_channels = static_cast<int>(filters.dimension(1));
        int k_h = static_cast<int>(filters.dimension(2));
        int k_w = static_cast<int>(filters.dimension(3));

        for (int oc = 0; oc < out_channels; ++oc) {
            for (int ic = 0; ic < in_channels; ++ic) {
                for (int y = 0; y < k_h; ++y) {
                    for (int x = 0; x < k_w; ++x) {
                        float weight = filters(oc, ic, y, x);
                        if (std::abs(weight) < threshold) continue;

                        SemanticEdge edge;
                        edge.source_token = "feature:in:" + std::to_string(ic);
                        edge.target_token = "feature:out:" + std::to_string(oc) + ":pos:" + std::to_string(y) + "," + std::to_string(x);
                        edge.weight = std::tanh(std::abs(weight));
                        edge.edge_type = "conv";
                        edge.layer_index = layer_idx;
                        graph.edges.push_back(edge);
                    }
                }
            }
        }
        return graph;
    }
};

// ==============================================================================
// 3. RNN/LSTM → TEMPORAL SEMANTIC EDGES
// ==============================================================================

class RNNExtractor {
public:
    static ExtractedGraph extract(
        const Eigen::MatrixXd& hidden_states,
        const Eigen::MatrixXd& recurrent_weights
    ) {
        ExtractedGraph graph;
        graph.architecture_type = "RNN";
        int seq_len = static_cast<int>(hidden_states.rows());

        for (int t = 0; t < seq_len - 1; ++t) {
            Eigen::VectorXd h_t = hidden_states.row(t);
            Eigen::VectorXd h_next = hidden_states.row(t + 1);
            Eigen::VectorXd predicted = recurrent_weights * h_t;
            
            double weight = 0.0;
            if (h_next.norm() > 1e-9 && predicted.norm() > 1e-9) {
                weight = h_next.dot(predicted) / (h_next.norm() * predicted.norm());
                weight = (weight + 1.0) / 2.0;
            }

            SemanticEdge edge;
            edge.source_id = static_cast<uint64_t>(t);
            edge.target_id = static_cast<uint64_t>(t + 1);
            edge.weight = weight;
            edge.edge_type = "recurrent";
            edge.layer_index = 0;
            graph.edges.push_back(edge);
        }
        return graph;
    }
};

// ==============================================================================
// 4. GRAPH NEURAL NETWORKS → DIRECT CONVERSION
// ==============================================================================

class GNNExtractor {
public:
    static ExtractedGraph extract(
        const Eigen::SparseMatrix<double>& adjacency_matrix,
        const std::vector<uint64_t>& node_ids
    ) {
        ExtractedGraph graph;
        graph.architecture_type = "GNN";
        for (int k = 0; k < adjacency_matrix.outerSize(); ++k) {
            for (Eigen::SparseMatrix<double>::InnerIterator it(adjacency_matrix, k); it; ++it) {
                SemanticEdge edge;
                edge.source_id = node_ids[static_cast<size_t>(it.row())];
                edge.target_id = node_ids[static_cast<size_t>(it.col())];
                edge.weight = it.value();
                edge.edge_type = "gnn";
                edge.layer_index = 0;
                graph.edges.push_back(edge);
            }
        }
        return graph;
    }
};

// ==============================================================================
// 5. SUBSTRATE CONVERTER
// ==============================================================================

class HartonomousConverter {
public:
    static void ingest_graph(
        Hartonomous::PostgresConnection& db,
        const ExtractedGraph& graph,
        const Hartonomous::BLAKE3Pipeline::Hash& model_id
    ) {
        using namespace Hartonomous;
        using namespace hartonomous::spatial;

        if (graph.edges.empty()) return;

        PostgresConnection::Transaction txn(db);

        auto get_comp_id = [&](const std::string& text) -> BLAKE3Pipeline::Hash {
            std::vector<uint8_t> data = {0x43};
            data.insert(data.end(), text.begin(), text.end());
            return BLAKE3Pipeline::hash(data.data(), data.size());
        };

        // Compute default centroid at origin of S3
        Eigen::Vector4d default_centroid(1, 0, 0, 0);

        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> comp_seen;
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;

        // Phase 1: Physicalities + Compositions (must flush before relations)
        {
            PhysicalityStore phys_store(db, true, true);
            CompositionStore comp_store(db, true, true);

            for (const auto& edge : graph.edges) {
                for (int side = 0; side < 2; ++side) {
                    std::string token = (side == 0)
                        ? (!edge.source_token.empty() ? edge.source_token : std::to_string(edge.source_id))
                        : (!edge.target_token.empty() ? edge.target_token : std::to_string(edge.target_id));

                    auto cid = get_comp_id(token);
                    if (!comp_seen.insert(cid).second) continue;

                    // Physicality for this composition
                    std::vector<uint8_t> pdata = {0x50};
                    pdata.insert(pdata.end(), reinterpret_cast<const uint8_t*>(default_centroid.data()),
                                 reinterpret_cast<const uint8_t*>(default_centroid.data()) + sizeof(double) * 4);
                    auto pid = BLAKE3Pipeline::hash(pdata.data(), pdata.size());

                    if (phys_seen.insert(pid).second) {
                        Eigen::Vector4d hc;
                        for (int d = 0; d < 4; ++d) hc[d] = (default_centroid[d] + 1.0) / 2.0;
                        phys_store.store({pid, HilbertCurve4D::encode(hc, HilbertCurve4D::EntityType::Composition),
                                          default_centroid, {}});
                    }
                    comp_store.store({cid, pid});
                }
            }
            phys_store.flush();
            comp_store.flush();
        }

        // Phase 2: Relations, sequences, ratings, evidence
        {
            PhysicalityStore phys_store(db, true, true);
            RelationStore rel_store(db, true, true);
            RelationSequenceStore rs_store(db, true, true);
            RelationRatingStore rating_store(db, true);
            RelationEvidenceStore ev_store(db, true, true);

            for (const auto& edge : graph.edges) {
                BLAKE3Pipeline::Hash sid, tid;
                if (!edge.source_token.empty()) sid = get_comp_id(edge.source_token);
                else sid = get_comp_id(std::to_string(edge.source_id));

                if (!edge.target_token.empty()) tid = get_comp_id(edge.target_token);
                else tid = get_comp_id(std::to_string(edge.target_id));

                bool s_first = std::memcmp(sid.data(), tid.data(), 16) < 0;
                const auto& lo = s_first ? sid : tid;
                const auto& hi = s_first ? tid : sid;

                uint8_t r_input[33];
                r_input[0] = 0x52;
                std::memcpy(r_input + 1, lo.data(), 16);
                std::memcpy(r_input + 17, hi.data(), 16);
                auto rid = BLAKE3Pipeline::hash(r_input, 33);

                // Relation physicality (centroid of source+target, both at default)
                std::vector<uint8_t> rpdata = {0x50};
                rpdata.insert(rpdata.end(), reinterpret_cast<const uint8_t*>(default_centroid.data()),
                              reinterpret_cast<const uint8_t*>(default_centroid.data()) + sizeof(double) * 4);
                auto rpid = BLAKE3Pipeline::hash(rpdata.data(), rpdata.size());

                if (phys_seen.insert(rpid).second) {
                    Eigen::Vector4d hc;
                    for (int d = 0; d < 4; ++d) hc[d] = (default_centroid[d] + 1.0) / 2.0;
                    phys_store.store({rpid, HilbertCurve4D::encode(hc, HilbertCurve4D::EntityType::Relation),
                                      default_centroid, {}});
                }

                rel_store.store({rid, rpid});

                // Relation sequence entries
                for (uint32_t ord = 0; ord < 2; ++ord) {
                    const auto& cid = (ord == 0) ? sid : tid;
                    uint8_t sdata[37]; sdata[0] = 0x54;
                    std::memcpy(sdata + 1, rid.data(), 16);
                    std::memcpy(sdata + 17, cid.data(), 16);
                    std::memcpy(sdata + 33, &ord, 4);
                    rs_store.store({BLAKE3Pipeline::hash(sdata, 37), rid, cid, ord, 1});
                }

                // Evidence with context-aware hash
                double strength = std::clamp(edge.weight, 0.0, 1.0);
                std::string tag = edge.edge_type;
                int32_t li = edge.layer_index;
                std::vector<uint8_t> ev_input;
                ev_input.insert(ev_input.end(), model_id.begin(), model_id.end());
                ev_input.insert(ev_input.end(), rid.begin(), rid.end());
                ev_input.insert(ev_input.end(), tag.begin(), tag.end());
                ev_input.insert(ev_input.end(), reinterpret_cast<uint8_t*>(&li),
                                reinterpret_cast<uint8_t*>(&li) + sizeof(li));
                auto evid = BLAKE3Pipeline::hash(ev_input.data(), ev_input.size());

                ev_store.store({evid, model_id, rid, true, (double)edge.to_elo(), strength});
                rating_store.store({rid, 1, (double)edge.to_elo(), 32.0});
            }

            phys_store.flush();
            rel_store.flush();
            rs_store.flush();
            rating_store.flush();
            ev_store.flush();
        }

        txn.commit();
    }
};

} // namespace hartonomous::ml
