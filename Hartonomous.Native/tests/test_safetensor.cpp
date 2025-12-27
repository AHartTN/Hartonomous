#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>
#include <catch2/benchmark/catch_benchmark.hpp>
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

// Real model path for testing
#ifndef TEST_DATA_DIR
#define TEST_DATA_DIR "."
#endif

static const std::string MINILM_MODEL_PATH =
    std::string(TEST_DATA_DIR) +
    "/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2/"
    "snapshots/c9745ed1d9f207416be6d2e6f8de32d1f16199bf";

namespace {
    // Schema validated once per test run
    static bool schema_validated = false;
    
    void ensure_schema_once() {
        if (schema_validated) return;
        
        SchemaManager mgr;
        auto status = mgr.ensure_schema();
        if (status.has_errors()) {
            throw std::runtime_error("Schema validation failed: " + status.summary());
        }
        
        // Seed atoms
        Seeder seeder(true);
        seeder.ensure_schema();
        
        schema_validated = true;
    }

    // Create a test safetensor file with DETERMINISTIC data
    // No randomness - exact counts specified
    void create_test_safetensor(const std::string& path,
                                 std::size_t dim1, std::size_t dim2,
                                 std::size_t non_zero_count) {
        SafetensorWriter writer;

        std::vector<float> dense(dim1 * dim2, 0.0f);

        // DETERMINISTIC: first N values are non-zero, rest are zero
        // No randomness, exact count, reproducible
        for (std::size_t i = 0; i < non_zero_count && i < dense.size(); ++i) {
            // Non-zero: alternating positive/negative, increasing magnitude
            dense[i] = static_cast<float>((i % 2 == 0 ? 1.0 : -1.0) * (0.1 + 0.01 * static_cast<double>(i)));
        }

        writer.add_tensor("test_layer.weight", {dim1, dim2}, dense);
        writer.write(path);
    }
}

TEST_CASE("SafetensorWriter creates valid file", "[safetensor]") {
    ensure_schema_once();

    std::string path = "test_output_safetensor.safetensors";

    SECTION("Write simple tensor") {
        SafetensorWriter writer;

        std::vector<float> data = {1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f};
        writer.add_tensor("simple", {2, 3}, data);
        writer.write(path);

        REQUIRE(std::filesystem::exists(path));
        REQUIRE(std::filesystem::file_size(path) > 0);
    }

    SECTION("Write multiple tensors") {
        SafetensorWriter writer;

        std::vector<float> data1 = {1.0f, 2.0f, 3.0f, 4.0f};
        std::vector<float> data2 = {5.0f, 6.0f, 7.0f, 8.0f, 9.0f, 10.0f};

        writer.add_tensor("layer1.weight", {2, 2}, data1);
        writer.add_tensor("layer2.weight", {2, 3}, data2);
        writer.write(path);

        REQUIRE(std::filesystem::exists(path));
    }

    std::filesystem::remove(path);
}

TEST_CASE("SafetensorReader reads written files", "[safetensor]") {
    ensure_schema_once();

    std::string path = "test_roundtrip.safetensors";

    // Write
    std::vector<float> original = {1.0f, 2.5f, -3.14f, 0.0f, 100.0f, -0.001f};
    {
        SafetensorWriter writer;
        writer.add_tensor("roundtrip_test", {2, 3}, original);
        writer.write(path);
    }

    // Read
    SafetensorReader reader(path);

    REQUIRE(reader.tensor_names().size() == 1);
    REQUIRE(reader.tensor_names()[0] == "roundtrip_test");

    const TensorMeta* meta = reader.get_tensor("roundtrip_test");
    REQUIRE(meta != nullptr);
    REQUIRE(meta->dtype == TensorDType::F32);
    REQUIRE(meta->shape.size() == 2);
    REQUIRE(meta->shape[0] == 2);
    REQUIRE(meta->shape[1] == 3);

    const float* data = reader.get_f32_data(*meta);
    for (std::size_t i = 0; i < original.size(); ++i) {
        REQUIRE_THAT(static_cast<double>(data[i]),
                     Catch::Matchers::WithinRel(static_cast<double>(original[i]), 1e-6));
    }

    std::filesystem::remove(path);
}

TEST_CASE("SafetensorImporter stores sparse weights", "[safetensor][db]") {
    ensure_schema_once();

    std::string path = "test_import.safetensors";

    // Create test file: 100x100 = 10,000 total weights, exactly 1000 non-zero
    create_test_safetensor(path, 100, 100, 1000);

    QueryStore store;
    SafetensorImporter importer(store, 1e-6);

    auto [total, stored] = importer.import_model(path);

    INFO("Total weights: " << total);
    INFO("Stored weights: " << stored);

    REQUIRE(total == 10000);
    REQUIRE(stored == 1000);  // Exactly 1000 non-zero values

    std::filesystem::remove(path);
}

TEST_CASE("Safetensor round-trip preserves salient weights", "[safetensor][db]") {
    ensure_schema_once();

    std::string input_path = "test_roundtrip_input.safetensors";
    std::string output_path = "test_roundtrip_output.safetensors";

    // Create input with known non-zero values
    {
        SafetensorWriter writer;
        std::vector<float> data(64, 0.0f);
        // Set specific salient values
        data[0] = 1.0f;
        data[10] = -0.5f;
        data[32] = 2.5f;
        data[63] = -1.0f;
        writer.add_tensor("weights", {8, 8}, data);
        writer.write(input_path);
    }

    // Import
    QueryStore store;
    SafetensorImporter importer(store, 1e-6);
    auto [total, stored] = importer.import_model(input_path);

    REQUIRE(total == 64);
    REQUIRE(stored == 4);  // Only 4 non-zero values

    // Export
    SafetensorExporter exporter(store);
    std::unordered_map<std::string, std::vector<std::size_t>> shapes;
    shapes["weights"] = {8, 8};
    exporter.export_model(output_path, importer.model_context(), shapes);

    // Verify export
    REQUIRE(std::filesystem::exists(output_path));

    std::filesystem::remove(input_path);
    std::filesystem::remove(output_path);
}

TEST_CASE("Safetensor performance benchmark", "[safetensor][!benchmark]") {
    ensure_schema_once();

    std::string path = "bench_safetensor.safetensors";

    SECTION("1M weights import < 10s") {
        // Create 1000x1000 = 1M weights, 90% sparse
        create_test_safetensor(path, 1000, 1000, 0.9);

        QueryStore store;
        SafetensorImporter importer(store, 1e-6);

        auto start = std::chrono::high_resolution_clock::now();
        auto [total, stored] = importer.import_model(path);
        auto end = std::chrono::high_resolution_clock::now();

        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();

        INFO("1M weights import: " << ms << " ms");
        INFO("Stored: " << stored << " / " << total);
        INFO("Sparsity: " << (1.0 - static_cast<double>(stored) / static_cast<double>(total)) * 100 << "%");

        REQUIRE(total == 1000000);
        REQUIRE(ms < 10000);  // < 10 seconds

        std::filesystem::remove(path);
    }
}

TEST_CASE("ModelIngester ingests real model package", "[model][db]") {
    ensure_schema_once();

    std::cerr << "DEBUG: Checking model path: " << MINILM_MODEL_PATH << std::endl;
    
    // Check if real model exists
    if (!std::filesystem::exists(MINILM_MODEL_PATH)) {
        SKIP("MiniLM model not found at: " << MINILM_MODEL_PATH);
    }
    
    std::cerr << "DEBUG: Model exists, creating store..." << std::endl;

    QueryStore store;
    
    std::cerr << "DEBUG: Store created, creating ingester..." << std::endl;
    
    ModelIngester ingester(store, 1e-6);

    std::cerr << "DEBUG: Ingester created, starting ingestion..." << std::endl;
    
    auto result = ingester.ingest_package(MINILM_MODEL_PATH);
    
    std::cerr << "DEBUG: Ingestion complete" << std::endl;

    INFO("=== MODEL PACKAGE INGESTION ===");
    INFO("Vocabulary: " << result.vocab.token_count << " tokens (" << result.vocab.ingested_count << " ingested)");
    INFO("Vocab time: " << result.vocab.duration.count() << " ms");
    INFO("Tensors: " << result.tensor_count);
    INFO("Total weights: " << result.total_weights);
    INFO("Stored weights: " << result.stored_weights);
    INFO("Sparsity: " << (result.sparsity_ratio * 100) << "%");
    INFO("Total time: " << result.total_duration.count() << " ms");
    INFO("==============================");

    // Verify vocabulary ingested
    REQUIRE(result.vocab.token_count > 0);
    REQUIRE(result.vocab.ingested_count > 0);

    // Verify tensors processed
    REQUIRE(result.tensor_count > 0);

    // Performance requirement: < 10 seconds for ~87MB model
    REQUIRE(result.total_duration.count() < 10000);
}

TEST_CASE("ModelIngester performance: all-MiniLM-L6-v2 < 10s", "[model][db][!benchmark]") {
    ensure_schema_once();

    if (!std::filesystem::exists(MINILM_MODEL_PATH)) {
        SKIP("MiniLM model not found");
    }

    std::cout << "\n=== ALL-MINILM-L6-V2 FULL PACKAGE INGESTION ===" << std::endl;

    QueryStore store;
    ModelIngester ingester(store, 1e-6);

    auto start = std::chrono::high_resolution_clock::now();
    auto result = ingester.ingest_package(MINILM_MODEL_PATH);
    auto end = std::chrono::high_resolution_clock::now();

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();

    std::cout << "Vocabulary:     " << result.vocab.token_count << " tokens" << std::endl;
    std::cout << "Vocab ingested: " << result.vocab.ingested_count << std::endl;
    std::cout << "Tensors:        " << result.tensor_count << std::endl;
    std::cout << "Total weights:  " << result.total_weights << std::endl;
    std::cout << "Stored weights: " << result.stored_weights << std::endl;
    std::cout << "Sparsity:       " << std::fixed << std::setprecision(1)
              << (result.sparsity_ratio * 100) << "%" << std::endl;
    std::cout << "Total time:     " << ms << " ms" << std::endl;
    std::cout << "================================================" << std::endl;

    // PERFORMANCE REQUIREMENT: < 10 seconds for 87MB model package
    REQUIRE(ms < 10000);
}

TEST_CASE("Embedding trajectory Frechet correlates with cosine similarity", "[safetensor][frechet]") {
    // Validate that storing embeddings as trajectories and using ST_FrechetDistance
    // produces results that correlate with cosine similarity
    
    std::string model_path = MINILM_MODEL_PATH + "/model.safetensors";
    
    if (!std::filesystem::exists(model_path)) {
        SKIP("MiniLM model not found at: " + model_path);
    }
    
    SafetensorReader reader(model_path);
    const TensorMeta* embed_meta = reader.get_tensor("embeddings.word_embeddings.weight");
    REQUIRE(embed_meta != nullptr);
    REQUIRE(embed_meta->shape.size() == 2);
    
    std::size_t vocab_size = embed_meta->shape[0];
    std::size_t hidden_dim = embed_meta->shape[1];
    const float* data = reader.get_f32_data(*embed_meta);
    
    INFO("Vocab: " << vocab_size << ", Dim: " << hidden_dim);
    
    // Compute cosine similarity
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
    
    // Compute max absolute difference (simplified Frechet for aligned curves)
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
    
    // Test multiple pairs and verify correlation
    // Similar embeddings should have: high cosine, low frechet
    // Different embeddings should have: low cosine, high frechet
    
    struct Sample { std::size_t a, b; double cos, frech; };
    std::vector<Sample> samples;
    
    // Sample pairs across vocabulary
    std::mt19937 rng(42);
    std::uniform_int_distribution<std::size_t> dist(0, std::min(vocab_size, std::size_t(10000)) - 1);
    
    for (int i = 0; i < 100; ++i) {
        std::size_t a = dist(rng);
        std::size_t b = dist(rng);
        if (a == b) continue;
        
        double c = cosine(a, b);
        double f = frechet_approx(a, b);
        samples.push_back({a, b, c, f});
    }
    
    // Compute Pearson correlation between cosine and -frechet
    // (negative because high cosine = low frechet)
    double sum_c = 0, sum_f = 0;
    for (const auto& s : samples) {
        sum_c += s.cos;
        sum_f += s.frech;
    }
    double mean_c = sum_c / samples.size();
    double mean_f = sum_f / samples.size();
    
    double cov = 0, var_c = 0, var_f = 0;
    for (const auto& s : samples) {
        double dc = s.cos - mean_c;
        double df = s.frech - mean_f;
        cov += dc * df;
        var_c += dc * dc;
        var_f += df * df;
    }
    
    double correlation = cov / (std::sqrt(var_c) * std::sqrt(var_f));
    
    std::cout << "Cosine vs Frechet correlation: " << correlation << std::endl;
    std::cout << "(Expected negative: high cosine = low frechet)" << std::endl;
    
    // Correlation should be negative and significant
    REQUIRE(correlation < -0.3);
}

