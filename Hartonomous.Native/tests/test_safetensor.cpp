/// =============================================================================
/// SAFETENSOR TESTS - Real model only, no fake data
/// =============================================================================

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>
#include "test_fixture.hpp"
#include "model/safetensor.hpp"
#include <cmath>
#include <iostream>
#include <iomanip>

using namespace hartonomous;
using namespace hartonomous::test;
using namespace hartonomous::model;

#ifndef TEST_DATA_DIR
#define TEST_DATA_DIR "."
#endif

static const std::string MINILM_PATH = std::string(TEST_DATA_DIR) +
    "/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2/"
    "snapshots/c9745ed1d9f207416be6d2e6f8de32d1f16199bf";

// =============================================================================
// SAFETENSOR READER
// =============================================================================

TEST_CASE("SafetensorReader: MiniLM structure", "[safetensor]") {
    std::string path = MINILM_PATH + "/model.safetensors";
    if (!std::filesystem::exists(path)) {
        SKIP("MiniLM not found: " + path);
    }
    
    SafetensorReader reader(path);
    auto names = reader.tensor_names();
    
    std::size_t total = 0;
    for (const auto& name : names) {
        const TensorMeta* m = reader.get_tensor(name);
        total += SafetensorReader::element_count(*m);
    }
    
    // MiniLM-L6-v2: ~22M params
    REQUIRE(total > 20000000);
    REQUIRE(total < 30000000);
}

TEST_CASE("SafetensorReader: embedding dimensions", "[safetensor]") {
    std::string path = MINILM_PATH + "/model.safetensors";
    if (!std::filesystem::exists(path)) {
        SKIP("MiniLM not found");
    }
    
    SafetensorReader reader(path);
    const TensorMeta* embed = reader.get_tensor("embeddings.word_embeddings.weight");
    
    REQUIRE(embed != nullptr);
    REQUIRE(embed->shape.size() == 2);
    REQUIRE(embed->shape[0] == 30522);  // BERT vocab
    REQUIRE(embed->shape[1] == 384);    // MiniLM hidden dim
}

TEST_CASE("SafetensorReader: weight distribution", "[safetensor]") {
    std::string path = MINILM_PATH + "/model.safetensors";
    if (!std::filesystem::exists(path)) {
        SKIP("MiniLM not found");
    }
    
    SafetensorReader reader(path);
    const TensorMeta* embed = reader.get_tensor("embeddings.word_embeddings.weight");
    REQUIRE(embed != nullptr);
    
    const float* data = reader.get_f32_data(*embed);
    std::size_t count = SafetensorReader::element_count(*embed);
    
    std::size_t near_zero = 0, small = 0, medium = 0, large = 0;
    for (std::size_t i = 0; i < count; ++i) {
        double w = std::abs(static_cast<double>(data[i]));
        if (w < 1e-6) near_zero++;
        else if (w < 0.01) small++;
        else if (w < 0.1) medium++;
        else large++;
    }
    
    REQUIRE(count == 30522 * 384);
    REQUIRE(medium > small);  // Real embeddings have this distribution
}

// =============================================================================
// MODEL INGESTION (uses cached singleton)
// =============================================================================

TEST_CASE("Model ingestion: performance", "[safetensor][model]") {
    REQUIRE_MODEL();
    
    const auto& result = TestEnv::model_result();
    
    // Verify real data
    REQUIRE(result.vocab.token_count == 30522);
    REQUIRE(result.tensor_count > 0);
    REQUIRE(result.stored_weights > 1000000);
    
    std::cerr << "\n=== MODEL STATS ===" << std::endl;
    std::cerr << "Vocab: " << result.vocab.token_count << std::endl;
    std::cerr << "Tensors: " << result.tensor_count << std::endl;
    std::cerr << "Weights: " << result.stored_weights << "/" << result.total_weights << std::endl;
    std::cerr << "===================" << std::endl;
}

