/// =============================================================================
/// MLOPS TESTS - Enterprise Grade
/// 
/// Uses TestEnv singleton - model ingested ONCE, cached forever.
/// No repeated work. No redundant database calls.
/// =============================================================================

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>
#include "test_fixture.hpp"
#include "mlops/mlops.hpp"

using namespace hartonomous;
using namespace hartonomous::test;
using namespace hartonomous::mlops;
using Catch::Matchers::WithinAbs;

// =============================================================================
// ATTENTION TESTS
// =============================================================================

TEST_CASE("Attention: find related concepts", "[mlops][attention]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto cat = store.encode_and_store("cat");
    auto dog = store.encode_and_store("dog");
    auto pet = store.encode_and_store("pet");
    auto animal = store.encode_and_store("animal");
    
    store.store_relationship(cat, pet, 0.9);
    store.store_relationship(dog, pet, 0.9);
    store.store_relationship(cat, animal, 0.8);
    store.store_relationship(dog, animal, 0.8);
    
    auto result = ops.attend(cat);
    REQUIRE(!result.attended.empty());
    
    bool found_pet = false, found_animal = false;
    for (const auto& n : result.attended) {
        if (n.ref.id_high == pet.id_high && n.ref.id_low == pet.id_low) found_pet = true;
        if (n.ref.id_high == animal.id_high && n.ref.id_low == animal.id_low) found_animal = true;
    }
    REQUIRE(found_pet);
    REQUIRE(found_animal);
}

TEST_CASE("Attention: weights sum to 1", "[mlops][attention]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto a = store.encode_and_store("alpha");
    auto b = store.encode_and_store("beta");
    store.store_relationship(a, b, 0.5);
    
    auto result = ops.attend(a);
    double sum = 0.0;
    for (const auto& n : result.attended) sum += n.attention_weight;
    REQUIRE_THAT(sum, WithinAbs(1.0, 0.01));
}

// =============================================================================
// GENERATION TESTS
// =============================================================================

TEST_CASE("Generation: produces ranked candidates", "[mlops][generation]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto his = store.encode_and_store("his");
    auto throne = store.encode_and_store("throne");
    auto chair = store.encode_and_store("chair");
    
    store.store_relationship(his, throne, 0.9);
    store.store_relationship(his, chair, 0.3);
    
    auto result = ops.generate(his);
    REQUIRE(!result.candidates.empty());
    
    double sum = 0.0;
    for (const auto& c : result.candidates) {
        REQUIRE(c.probability >= 0.0);
        REQUIRE(c.probability <= 1.0);
        sum += c.probability;
    }
    REQUIRE_THAT(sum, WithinAbs(1.0, 0.01));
}

TEST_CASE("Generation: stronger edge ranks higher", "[mlops][generation]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto x = store.encode_and_store("xgen");
    auto high = store.encode_and_store("high_weight");
    auto low = store.encode_and_store("low_weight");
    
    store.store_relationship(x, high, 0.95);
    store.store_relationship(x, low, 0.1);
    
    auto result = ops.generate(x);
    
    double high_prob = 0.0, low_prob = 0.0;
    for (const auto& c : result.candidates) {
        if (c.ref.id_high == high.id_high) high_prob = c.probability;
        if (c.ref.id_high == low.id_high) low_prob = c.probability;
    }
    REQUIRE(high_prob > low_prob);
}

// =============================================================================
// INFERENCE TESTS
// =============================================================================

TEST_CASE("Inference: single hop", "[mlops][inference]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto cat = store.encode_and_store("cat_inf");
    auto mammal = store.encode_and_store("mammal_inf");
    store.store_relationship(cat, mammal, 0.9);
    
    auto result = ops.infer(cat, 1);
    REQUIRE(result.success());
    REQUIRE(result.path.size() >= 1);
}

TEST_CASE("Inference: multi-hop chain", "[mlops][inference]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto a = store.encode_and_store("chain_a");
    auto b = store.encode_and_store("chain_b");
    auto c = store.encode_and_store("chain_c");
    auto d = store.encode_and_store("chain_d");
    
    store.store_relationship(a, b, 0.9);
    store.store_relationship(b, c, 0.9);
    store.store_relationship(c, d, 0.9);
    
    auto result = ops.infer(a, 3);
    REQUIRE(result.success());
    REQUIRE(result.total_weight > 0);
}

TEST_CASE("Inference: isolated node fails", "[mlops][inference]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto isolated = store.encode_and_store("totally_isolated_node_xyz123");
    auto result = ops.infer(isolated, 3);
    REQUIRE(!result.success());
}

// =============================================================================
// MODEL INGESTION - Uses cached singleton, runs ONCE
// =============================================================================

TEST_CASE("Model ingestion: vocabulary", "[mlops][model]") {
    REQUIRE_MODEL();
    
    const auto& result = TestEnv::model_result();
    REQUIRE(result.vocab.token_count == 30522);  // BERT vocab
    REQUIRE(result.vocab.ingested_count > 0);
}

TEST_CASE("Model ingestion: tensors", "[mlops][model]") {
    REQUIRE_MODEL();
    
    const auto& result = TestEnv::model_result();
    REQUIRE(result.tensor_count > 0);
    REQUIRE(result.total_weights > 10000000);
    REQUIRE(result.stored_weights > 1000000);
}

TEST_CASE("Model ingestion: relationships created", "[mlops][model]") {
    REQUIRE_MODEL();
    
    auto count = TestEnv::store().relationship_count();
    REQUIRE(count > 0);
}

TEST_CASE("Model ingestion: vocabulary decodable", "[mlops][model]") {
    REQUIRE_MODEL();
    
    auto& store = TestEnv::store();
    const auto& vocab = TestEnv::ingester().vocabulary();
    REQUIRE(!vocab.empty());
    
    // Test first 10 tokens
    int tested = 0;
    for (std::size_t i = 0; i < vocab.size() && tested < 10; ++i) {
        const auto& token = vocab[i];
        if (token.ref.id_high == 0 && token.ref.id_low == 0) continue;
        if (token.text.empty()) continue;
        
        auto decoded = store.decode_string(token.ref);
        REQUIRE(decoded == token.text);
        tested++;
    }
    REQUIRE(tested > 0);
}

// =============================================================================
// SEQUENCE GENERATION
// =============================================================================

TEST_CASE("Sequence generation: follows chain", "[mlops][sequence]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto once = store.encode_and_store("once_seq");
    auto upon = store.encode_and_store("upon_seq");
    auto a = store.encode_and_store("a_seq");
    auto time = store.encode_and_store("time_seq");
    
    store.store_relationship(once, upon, 0.95);
    store.store_relationship(upon, a, 0.9);
    store.store_relationship(a, time, 0.85);
    
    std::vector<NodeRef> generated;
    NodeRef current = once;
    
    for (int i = 0; i < 5 && (current.id_high != 0 || current.id_low != 0); ++i) {
        auto next = ops.generate_next(current, 0.0, 42);
        if (next.id_high == 0 && next.id_low == 0) break;
        generated.push_back(next);
        current = next;
    }
    
    REQUIRE(generated.size() >= 1);
}

