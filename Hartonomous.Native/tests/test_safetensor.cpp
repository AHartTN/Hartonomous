/// SAFETENSOR TESTS - Real model ingestion only. No fake data.
///
/// Tests verify:
/// 1. Real MiniLM model file can be read
/// 2. Real vocabulary tokens map to real embeddings
/// 3. Sparse encoding works on real weight distributions
/// 4. Frechet distance correlates with cosine similarity on real embeddings

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>
#include "../src/model/safetensor.hpp"
#include "../src/model/model_ingest.hpp"
#include "../src/db/schema_manager.hpp"
#include "../src/db/seeder.hpp"
#include <filesystem>
#include <random>
#include <cmath>
#include <iostream>
#include <iomanip>

using namespace hartonomous;
using namespace hartonomous::model;
using namespace hartonomous::db;

#ifndef TEST_DATA_DIR
#define TEST_DATA_DIR "."
#endif

static const std::string MINILM_MODEL_PATH =
    std::string(TEST_DATA_DIR) +
    "/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2/"
    "snapshots/c9745ed1d9f207416be6d2e6f8de32d1f16199bf";

namespace {
    static bool schema_validated = false;
    
    void ensure_schema_once() {
        if (schema_validated) return;
        
        SchemaManager mgr;
        auto status = mgr.ensure_schema();
        if (status.has_errors()) {
            throw std::runtime_error("Schema validation failed: " + status.summary());
        }
        
        Seeder seeder(true);
        seeder.ensure_schema();
        
        schema_validated = true;
    }
}

// =============================================================================
// REAL MODEL TESTS - MiniLM-L6-v2
// =============================================================================

TEST_CASE("Read real MiniLM model.safetensors", "[safetensor][model]") {
    std::string model_path = MINILM_MODEL_PATH + "/model.safetensors";
    
    if (!std::filesystem::exists(model_path)) {
        SKIP("MiniLM model.safetensors not found at: " + model_path);
    }

    SafetensorReader reader(model_path);
    auto names = reader.tensor_names();

    std::cout << "\n=== MiniLM-L6-v2 TENSORS ===" << std::endl;
    std::size_t total_params = 0;
    for (const auto& name : names) {
        const TensorMeta* meta = reader.get_tensor(name);
        std::size_t count = SafetensorReader::element_count(*meta);
        total_params += count;
        
        std::cout << "  " << name << ": [";
        for (std::size_t i = 0; i < meta->shape.size(); ++i) {
            if (i > 0) std::cout << ", ";
            std::cout << meta->shape[i];
        }
        std::cout << "] = " << count << " params" << std::endl;
    }
    std::cout << "Total parameters: " << total_params << std::endl;
    std::cout << "============================" << std::endl;

    // MiniLM-L6-v2 has ~22M parameters
    REQUIRE(total_params > 20000000);
    REQUIRE(total_params < 30000000);
    
    // Must have word embeddings
    const TensorMeta* embed = reader.get_tensor("embeddings.word_embeddings.weight");
    REQUIRE(embed != nullptr);
    REQUIRE(embed->shape.size() == 2);
    REQUIRE(embed->shape[0] == 30522);  // BERT vocabulary size
    REQUIRE(embed->shape[1] == 384);    // MiniLM hidden dimension
}

TEST_CASE("Real model weight distribution analysis", "[safetensor][model]") {
    std::string model_path = MINILM_MODEL_PATH + "/model.safetensors";
    
    if (!std::filesystem::exists(model_path)) {
        SKIP("MiniLM model not found");
    }

    SafetensorReader reader(model_path);
    
    // Analyze word embeddings sparsity
    const TensorMeta* embed = reader.get_tensor("embeddings.word_embeddings.weight");
    REQUIRE(embed != nullptr);
    
    const float* data = reader.get_f32_data(*embed);
    std::size_t count = SafetensorReader::element_count(*embed);
    
    // Count weights by magnitude
    std::size_t near_zero = 0;      // |w| < 1e-6
    std::size_t small = 0;          // 1e-6 <= |w| < 0.01
    std::size_t medium = 0;         // 0.01 <= |w| < 0.1
    std::size_t large = 0;          // |w| >= 0.1
    
    double sum_sq = 0.0;
    for (std::size_t i = 0; i < count; ++i) {
        double w = std::abs(static_cast<double>(data[i]));
        sum_sq += w * w;
        
        if (w < 1e-6) near_zero++;
        else if (w < 0.01) small++;
        else if (w < 0.1) medium++;
        else large++;
    }
    
    double rms = std::sqrt(sum_sq / static_cast<double>(count));
    
    std::cout << "\n=== EMBEDDING WEIGHT DISTRIBUTION ===" << std::endl;
    std::cout << "Total weights: " << count << std::endl;
    std::cout << "Near-zero (|w| < 1e-6): " << near_zero << " (" 
              << std::fixed << std::setprecision(2) 
              << (100.0 * near_zero / count) << "%)" << std::endl;
    std::cout << "Small (1e-6 <= |w| < 0.01): " << small << " (" 
              << (100.0 * small / count) << "%)" << std::endl;
    std::cout << "Medium (0.01 <= |w| < 0.1): " << medium << " (" 
              << (100.0 * medium / count) << "%)" << std::endl;
    std::cout << "Large (|w| >= 0.1): " << large << " (" 
              << (100.0 * large / count) << "%)" << std::endl;
    std::cout << "RMS: " << rms << std::endl;
    std::cout << "======================================" << std::endl;
    
    // Real embeddings have a distribution - verify we measured something
    REQUIRE(count == 30522 * 384);  // vocab_size * hidden_dim
}

TEST_CASE("Ingest real MiniLM vocabulary and embeddings", "[safetensor][model][db]") {
    ensure_schema_once();
    
    if (!std::filesystem::exists(MINILM_MODEL_PATH)) {
        SKIP("MiniLM model not found at: " + MINILM_MODEL_PATH);
    }

    QueryStore store;
    
    // Keep top 50% of weights by magnitude
    ModelIngester ingester(store, 50.0);

    auto start = std::chrono::high_resolution_clock::now();
    auto result = ingester.ingest_package(MINILM_MODEL_PATH);
    auto end = std::chrono::high_resolution_clock::now();
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();

    std::cout << "\n=== REAL MODEL INGESTION ===" << std::endl;
    std::cout << "Vocabulary tokens: " << result.vocab.token_count << std::endl;
    std::cout << "Tokens ingested: " << result.vocab.ingested_count << std::endl;
    std::cout << "Tensors processed: " << result.tensor_count << std::endl;
    std::cout << "Total weights: " << result.total_weights << std::endl;
    std::cout << "Salient weights stored (top 50%): " << result.stored_weights << std::endl;
    std::cout << "Sparsity: " << std::fixed << std::setprecision(1) 
              << (result.sparsity_ratio * 100) << "%" << std::endl;
    std::cout << "Time: " << ms << " ms" << std::endl;
    std::cout << "=============================" << std::endl;

    // Real assertions on real data
    REQUIRE(result.vocab.token_count == 30522);  // BERT vocab
    REQUIRE(result.vocab.ingested_count > 0);
    REQUIRE(result.tensor_count > 0);
    REQUIRE(result.total_weights > 10000000);  // ~11M params (excluding embeddings)
    REQUIRE(result.stored_weights > 1000000);  // >1M salient weights
    REQUIRE(ms < 120000);  // Under 2 minutes
}

TEST_CASE("Frechet distance correlates with cosine similarity on real embeddings", "[safetensor][model]") {
    std::string model_path = MINILM_MODEL_PATH + "/model.safetensors";
    
    if (!std::filesystem::exists(model_path)) {
        SKIP("MiniLM model not found at: " + model_path);
    }
    
    SafetensorReader reader(model_path);
    const TensorMeta* embed_meta = reader.get_tensor("embeddings.word_embeddings.weight");
    REQUIRE(embed_meta != nullptr);
    
    std::size_t vocab_size = embed_meta->shape[0];
    std::size_t hidden_dim = embed_meta->shape[1];
    const float* data = reader.get_f32_data(*embed_meta);
    
    std::cout << "\n=== FRECHET vs COSINE CORRELATION ===" << std::endl;
    std::cout << "Vocabulary: " << vocab_size << " tokens" << std::endl;
    std::cout << "Hidden dim: " << hidden_dim << std::endl;
    
    // Cosine similarity between two embeddings
    auto cosine = [&](std::size_t a, std::size_t b) -> double {
        const float* va = data + a * hidden_dim;
        const float* vb = data + b * hidden_dim;
        double dot = 0, na = 0, nb = 0;
        for (std::size_t i = 0; i < hidden_dim; ++i) {
            dot += static_cast<double>(va[i]) * static_cast<double>(vb[i]);
            na += static_cast<double>(va[i]) * static_cast<double>(va[i]);
            nb += static_cast<double>(vb[i]) * static_cast<double>(vb[i]);
        }
        return dot / (std::sqrt(na) * std::sqrt(nb));
    };
    
    // Max absolute difference (simplified aligned Frechet)
    auto frechet_approx = [&](std::size_t a, std::size_t b) -> double {
        const float* va = data + a * hidden_dim;
        const float* vb = data + b * hidden_dim;
        double maxdiff = 0;
        for (std::size_t i = 0; i < hidden_dim; ++i) {
            double diff = std::abs(static_cast<double>(va[i]) - static_cast<double>(vb[i]));
            if (diff > maxdiff) maxdiff = diff;
        }
        return maxdiff;
    };
    
    // Sample 100 random pairs
    std::mt19937 rng(42);
    std::uniform_int_distribution<std::size_t> dist(0, vocab_size - 1);
    
    std::vector<std::pair<double, double>> samples;  // (cosine, frechet)
    for (int i = 0; i < 100; ++i) {
        std::size_t a = dist(rng);
        std::size_t b = dist(rng);
        if (a == b) continue;
        
        samples.emplace_back(cosine(a, b), frechet_approx(a, b));
    }
    
    // Compute Pearson correlation
    double sum_c = 0, sum_f = 0;
    for (const auto& [c, f] : samples) {
        sum_c += c;
        sum_f += f;
    }
    double mean_c = sum_c / samples.size();
    double mean_f = sum_f / samples.size();
    
    double cov = 0, var_c = 0, var_f = 0;
    for (const auto& [c, f] : samples) {
        double dc = c - mean_c;
        double df = f - mean_f;
        cov += dc * df;
        var_c += dc * dc;
        var_f += df * df;
    }
    
    double correlation = cov / (std::sqrt(var_c) * std::sqrt(var_f));
    
    std::cout << "Samples: " << samples.size() << std::endl;
    std::cout << "Correlation: " << std::fixed << std::setprecision(4) << correlation << std::endl;
    std::cout << "(Expected negative: high cosine = low Frechet)" << std::endl;
    std::cout << "=======================================" << std::endl;
    
    // Correlation should be negative (high similarity = low distance)
    REQUIRE(correlation < -0.3);
}

