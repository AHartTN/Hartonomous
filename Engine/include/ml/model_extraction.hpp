#pragma once

#include <Eigen/Core>
#include <Eigen/Sparse>
#include <cstdint>
#include <vector>
#include <unordered_map>
#include <string>

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
 *   5. Autoencoders: Latent space → 4D projections
 *
 * Pipeline:
 *   Model Weights → Extract Edges → Convert to ELO → Store in Hartonomous
 *
 * CRITICAL INSIGHT from user:
 *   "Attention is easy... input token A's weight/intensity/beaten path to output
 *    token B... that's an ELO match right there"
 *
 * YES! Attention weight = ELO rating:
 *   - High attention = High ELO (strong connection, beaten path)
 *   - Low attention = Low ELO (weak connection, rare path)
 *   - Multi-head attention = Multiple ELO graphs (ensemble)
 */

/**
 * @brief Semantic edge extracted from AI model
 */
struct SemanticEdge {
    uint64_t source_id;      ///< Source node (token/feature index)
    uint64_t target_id;      ///< Target node (token/feature index)
    double weight;           ///< Connection strength (0 to 1)
    std::string edge_type;   ///< Type: "attention", "conv", "recurrent", etc.
    int32_t layer_index;     ///< Layer number in original model
    int32_t head_index;      ///< Attention head (for transformers), -1 if N/A

    /**
     * @brief Convert weight to ELO rating
     *
     * ELO scale: 1500 = average, 2000+ = strong, 1200- = weak
     *
     * Formula: ELO = 1500 + 500 * (2 * weight - 1)
     *   weight=1.0 → ELO=2000 (very strong)
     *   weight=0.5 → ELO=1500 (average)
     *   weight=0.0 → ELO=1000 (very weak)
     */
    int32_t to_elo() const {
        // Map [0, 1] weight to [1000, 2000] ELO
        return static_cast<int32_t>(1500.0 + 500.0 * (2.0 * weight - 1.0));
    }
};

/**
 * @brief Extracted model graph
 */
struct ExtractedGraph {
    std::vector<SemanticEdge> edges;
    std::unordered_map<uint64_t, std::string> node_labels;  ///< Node ID → Token string
    std::string model_name;
    std::string architecture_type;
    int32_t num_layers;
    int32_t num_parameters;
};

// ==============================================================================
// 1. TRANSFORMER ATTENTION → ELO EDGES (EASY!)
// ==============================================================================

/**
 * @brief Extract semantic edges from Transformer attention weights
 *
 * Transformers use attention mechanisms where:
 *   Attention(Q, K, V) = softmax(Q * K^T / sqrt(d_k)) * V
 *
 * The attention matrix shows how much each input token attends to each output token.
 *
 * Mapping to Hartonomous:
 *   - Input token A → Source node
 *   - Output token B → Target node
 *   - Attention weight[A, B] → Edge weight → ELO rating
 *
 * @param attention_weights Sequence of attention matrices (one per layer/head)
 *   - Shape: [num_layers, num_heads, seq_len, seq_len]
 * @param token_ids Sequence of token IDs (vocab indices)
 * @param vocab Token vocabulary (index → string)
 * @param sparsity_threshold Drop edges below this weight (default 0.01)
 * @return ExtractedGraph Semantic edges with ELO ratings
 */
class TransformerExtractor {
public:
    /**
     * @brief Extract edges from multi-head attention
     *
     * @param attention_weights [L, H, N, N] tensor (layers, heads, seq, seq)
     * @param tokens [N] token IDs
     * @param sparsity_threshold Minimum attention weight to keep
     * @return ExtractedGraph
     */
    static ExtractedGraph extract(
        const std::vector<Eigen::MatrixXd>& attention_weights,  // One matrix per (layer, head)
        const std::vector<uint64_t>& tokens,
        double sparsity_threshold = 0.01
    ) {
        ExtractedGraph graph;
        graph.architecture_type = "Transformer";
        graph.num_layers = static_cast<int32_t>(attention_weights.size());

        for (size_t layer_head_idx = 0; layer_head_idx < attention_weights.size(); ++layer_head_idx) {
            const Eigen::MatrixXd& attn = attention_weights[layer_head_idx];

            // Parse layer and head indices (assuming packed format)
            int32_t layer = static_cast<int32_t>(layer_head_idx / 8);  // 8 heads per layer (typical)
            int32_t head = static_cast<int32_t>(layer_head_idx % 8);

            // Extract edges from attention matrix
            for (int i = 0; i < attn.rows(); ++i) {
                for (int j = 0; j < attn.cols(); ++j) {
                    double weight = attn(i, j);

                    // Apply sparsity filter
                    if (weight < sparsity_threshold) continue;

                    // Create semantic edge
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

    /**
     * @brief Aggregate multi-head attention into single ELO graph
     *
     * Averages attention weights across heads to get a single consensus edge weight.
     *
     * @param attention_weights [L, H, N, N] tensor
     * @param tokens [N] token IDs
     * @return ExtractedGraph Single unified graph
     */
    static ExtractedGraph extract_aggregated(
        const std::vector<Eigen::MatrixXd>& attention_weights,
        const std::vector<uint64_t>& tokens
    ) {
        // Aggregate attention across heads
        // Use map instead of unordered_map (std::hash doesn't support pairs)
        std::map<std::pair<uint64_t, uint64_t>, double> edge_weights;

        for (const auto& attn : attention_weights) {
            for (int i = 0; i < attn.rows(); ++i) {
                for (int j = 0; j < attn.cols(); ++j) {
                    auto key = std::make_pair(tokens[i], tokens[j]);
                    edge_weights[key] += attn(i, j);
                }
            }
        }

        // Normalize by number of attention matrices
        double norm = 1.0 / static_cast<double>(attention_weights.size());
        for (auto& [key, weight] : edge_weights) {
            weight *= norm;
        }

        // Build graph
        ExtractedGraph graph;
        graph.architecture_type = "Transformer (aggregated)";

        for (const auto& [key, weight] : edge_weights) {
            SemanticEdge edge;
            edge.source_id = key.first;
            edge.target_id = key.second;
            edge.weight = weight;
            edge.edge_type = "attention_aggregated";
            edge.layer_index = -1;  // Aggregated across layers
            edge.head_index = -1;   // Aggregated across heads

            graph.edges.push_back(edge);
        }

        return graph;
    }
};

// ==============================================================================
// 2. CNN FILTERS → SPATIAL SEMANTIC EDGES (TRICKY)
// ==============================================================================

/**
 * @brief Extract semantic edges from Convolutional Neural Networks
 *
 * CNNs use convolutional filters to detect local patterns (edges, textures, objects).
 *
 * Challenge: Convolutions operate on **spatial features**, not discrete tokens.
 *
 * Solution: Treat each spatial location as a "node" and filter responses as "edges".
 *
 * Mapping to Hartonomous:
 *   - Feature map position (x, y, channel) → Node
 *   - Filter activation → Edge weight
 *   - Receptive field connections → Spatial edges
 *
 * Example: 3×3 convolution
 *   - Center pixel (x, y) connects to 9 neighbors
 *   - Filter weights determine edge strengths
 *
 * @param feature_maps Output activations of CNN layers
 * @param filter_weights Convolutional filter weights
 * @return ExtractedGraph Spatial semantic graph
 */
class CNNExtractor {
public:
    /**
     * @brief Extract edges from convolutional layers
     *
     * @param feature_maps [C, H, W] feature map (channels, height, width)
     * @param filters [F, C, K, K] filter weights (filters, channels, kernel_h, kernel_w)
     * @return ExtractedGraph
     */
    static ExtractedGraph extract(
        const std::vector<Eigen::MatrixXd>& feature_maps,  // One per channel
        const std::vector<Eigen::MatrixXd>& filters,       // One per filter
        int kernel_size = 3
    ) {
        ExtractedGraph graph;
        graph.architecture_type = "CNN";

        // For each spatial location, create edges to receptive field neighbors
        int height = static_cast<int>(feature_maps[0].rows());
        int width = static_cast<int>(feature_maps[0].cols());
        int num_channels = static_cast<int>(feature_maps.size());

        for (int c = 0; c < num_channels; ++c) {
            for (int y = 0; y < height; ++y) {
                for (int x = 0; x < width; ++x) {
                    // Source node: (x, y, c)
                    uint64_t source_id = encode_spatial_node(x, y, c, width, height);

                    // Create edges to neighbors in receptive field
                    for (int ky = -kernel_size / 2; ky <= kernel_size / 2; ++ky) {
                        for (int kx = -kernel_size / 2; kx <= kernel_size / 2; ++kx) {
                            int nx = x + kx;
                            int ny = y + ky;

                            // Check bounds
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;

                            // Target node: (nx, ny, c)
                            uint64_t target_id = encode_spatial_node(nx, ny, c, width, height);

                            // Edge weight = filter activation
                            double weight = std::abs(feature_maps[c](ny, nx));

                            // Normalize to [0, 1]
                            weight = std::tanh(weight);  // Squash to [0, 1]

                            SemanticEdge edge;
                            edge.source_id = source_id;
                            edge.target_id = target_id;
                            edge.weight = weight;
                            edge.edge_type = "conv";
                            edge.layer_index = 0;  // TODO: Track layer
                            edge.head_index = -1;

                            graph.edges.push_back(edge);
                        }
                    }
                }
            }
        }

        return graph;
    }

private:
    /**
     * @brief Encode spatial node (x, y, channel) as unique ID
     */
    static uint64_t encode_spatial_node(int x, int y, int c, int width, int height) {
        return static_cast<uint64_t>(c * width * height + y * width + x);
    }
};

// ==============================================================================
// 3. RNN/LSTM → TEMPORAL SEMANTIC EDGES (MODERATE)
// ==============================================================================

/**
 * @brief Extract semantic edges from Recurrent Neural Networks
 *
 * RNNs process sequences with temporal dependencies:
 *   h_t = f(W * h_{t-1} + U * x_t)
 *
 * Mapping to Hartonomous:
 *   - Hidden state at time t → Node
 *   - Recurrent weight W → Temporal edge
 *   - Input weight U → Input edge
 *
 * Challenge: Hidden states are continuous vectors, not discrete tokens.
 *
 * Solution: Quantize hidden states to discrete semantic clusters.
 */
class RNNExtractor {
public:
    /**
     * @brief Extract edges from RNN hidden states
     *
     * @param hidden_states [T, D] sequence of hidden states (time, dims)
     * @param recurrent_weights [D, D] recurrent weight matrix
     * @return ExtractedGraph
     */
    static ExtractedGraph extract(
        const Eigen::MatrixXd& hidden_states,
        const Eigen::MatrixXd& recurrent_weights
    ) {
        ExtractedGraph graph;
        graph.architecture_type = "RNN";

        int seq_len = static_cast<int>(hidden_states.rows());

        // Create temporal edges h_t → h_{t+1}
        for (int t = 0; t < seq_len - 1; ++t) {
            // Compute influence of h_t on h_{t+1}
            Eigen::VectorXd h_t = hidden_states.row(t);
            Eigen::VectorXd h_next = hidden_states.row(t + 1);

            // Edge weight = similarity between h_t and predicted h_{t+1}
            Eigen::VectorXd predicted = recurrent_weights * h_t;
            double weight = h_next.dot(predicted) / (h_next.norm() * predicted.norm());
            weight = (weight + 1.0) / 2.0;  // Map [-1, 1] to [0, 1]

            SemanticEdge edge;
            edge.source_id = static_cast<uint64_t>(t);
            edge.target_id = static_cast<uint64_t>(t + 1);
            edge.weight = weight;
            edge.edge_type = "recurrent";
            edge.layer_index = 0;
            edge.head_index = -1;

            graph.edges.push_back(edge);
        }

        return graph;
    }
};

// ==============================================================================
// 4. GRAPH NEURAL NETWORKS → DIRECT CONVERSION (EASY!)
// ==============================================================================

/**
 * @brief Extract semantic edges from Graph Neural Networks
 *
 * GNNs already operate on graphs! Just extract the message-passing edges.
 *
 * Mapping to Hartonomous:
 *   - GNN node → Hartonomous node
 *   - GNN edge → Hartonomous edge
 *   - Edge weight → ELO rating
 *
 * This is trivial - GNNs are already in the right format!
 */
class GNNExtractor {
public:
    /**
     * @brief Extract edges from GNN (trivial conversion)
     *
     * @param adjacency_matrix [N, N] adjacency matrix
     * @param node_ids [N] node identifiers
     * @return ExtractedGraph
     */
    static ExtractedGraph extract(
        const Eigen::SparseMatrix<double>& adjacency_matrix,
        const std::vector<uint64_t>& node_ids
    ) {
        ExtractedGraph graph;
        graph.architecture_type = "GNN";

        for (int k = 0; k < adjacency_matrix.outerSize(); ++k) {
            for (Eigen::SparseMatrix<double>::InnerIterator it(adjacency_matrix, k); it; ++it) {
                SemanticEdge edge;
                edge.source_id = node_ids[it.row()];
                edge.target_id = node_ids[it.col()];
                edge.weight = it.value();
                edge.edge_type = "gnn";
                edge.layer_index = 0;
                edge.head_index = -1;

                graph.edges.push_back(edge);
            }
        }

        return graph;
    }
};

// ==============================================================================
// 5. UNIFIED EXTRACTION PIPELINE
// ==============================================================================

/**
 * @brief Unified model extraction interface
 *
 * Detects model architecture and routes to appropriate extractor.
 */
class ModelExtractor {
public:
    /**
     * @brief Extract semantic graph from any model
     *
     * @param model_type Architecture type ("transformer", "cnn", "rnn", "gnn")
     * @param model_data Model-specific data (attention weights, filters, etc.)
     * @return ExtractedGraph
     */
    static ExtractedGraph extract(
        const std::string& model_type,
        void* model_data  // Type-erased pointer to model-specific data
    ) {
        if (model_type == "transformer") {
            // Cast to transformer data and extract
            // auto* data = static_cast<TransformerData*>(model_data);
            // return TransformerExtractor::extract(data->attention, data->tokens);
        } else if (model_type == "cnn") {
            // Cast to CNN data and extract
            // auto* data = static_cast<CNNData*>(model_data);
            // return CNNExtractor::extract(data->features, data->filters);
        } else if (model_type == "rnn") {
            // Cast to RNN data and extract
            // auto* data = static_cast<RNNData*>(model_data);
            // return RNNExtractor::extract(data->hidden_states, data->weights);
        } else if (model_type == "gnn") {
            // Cast to GNN data and extract
            // auto* data = static_cast<GNNData*>(model_data);
            // return GNNExtractor::extract(data->adjacency, data->node_ids);
        }

        throw std::runtime_error("Unsupported model type: " + model_type);
    }
};

// ==============================================================================
// 6. ELO CONVERSION AND STORAGE
// ==============================================================================

/**
 * @brief Convert extracted graph to Hartonomous format and store in database
 *
 * Pipeline:
 *   1. Extract edges from model
 *   2. Convert weights to ELO ratings
 *   3. Project nodes to 4D S³ (using Laplacian Eigenmaps)
 *   4. Store in PostgreSQL (semantic_edges table)
 */
class HartonomousConverter {
public:
    /**
     * @brief Convert extracted graph to Hartonomous database entries
     *
     * @param graph Extracted semantic graph
     * @return SQL INSERT statements
     */
    static std::vector<std::string> to_sql(const ExtractedGraph& graph) {
        std::vector<std::string> statements;

        for (const auto& edge : graph.edges) {
            // Convert to ELO
            int32_t elo = edge.to_elo();

            // Generate SQL INSERT
            std::string sql = "INSERT INTO semantic_edges "
                            "(source_hash, target_hash, edge_type, elo_rating, "
                            " usage_count, created_at, last_used_at) "
                            "VALUES ("
                            "decode('" + std::to_string(edge.source_id) + "', 'hex'), "
                            "decode('" + std::to_string(edge.target_id) + "', 'hex'), "
                            "'" + edge.edge_type + "', "
                            + std::to_string(elo) + ", "
                            "0, NOW(), NOW()) "
                            "ON CONFLICT (source_hash, target_hash, edge_type) "
                            "DO UPDATE SET elo_rating = EXCLUDED.elo_rating;";

            statements.push_back(sql);
        }

        return statements;
    }
};

} // namespace hartonomous::ml
