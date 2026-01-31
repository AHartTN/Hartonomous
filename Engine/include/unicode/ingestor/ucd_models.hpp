#pragma once

#include <string>
#include <vector>
#include <array>
#include <cstdint>
#include <Eigen/Core>

namespace Hartonomous::unicode {

struct UCAWeights {
    uint32_t primary;
    uint32_t secondary;
    uint32_t tertiary;
    uint32_t quaternary;
};

struct SemanticEdge {
    uint32_t target_cp;
    uint32_t weight;
    std::string type;
};

struct CodepointMetadata {
    uint32_t codepoint;
    std::string name;
    std::string general_category;
    uint8_t combining_class;
    std::string script;
    std::string block;
    std::string age;
    std::string decomposition;
    
    // Han Radical/Stroke
    uint32_t radical = 0;
    int32_t strokes = 0;
    
    // Semantic clustering
    uint32_t base_codepoint = 0;
    
    // UCA Weights from DUCET
    std::vector<UCAWeights> uca_elements;
    
    // Graph Adjacency
    std::vector<SemanticEdge> edges;
    
    // Semantic Buckets (for 1D Sequence)
    uint32_t primary_group = 0;
    uint32_t script_group = 0;
    
    // Deterministic Sequence Index
    uint32_t sequence_index = 0;
    
    // 4D Embedding on S3
    Eigen::Vector4d position;
};

// Weight Tiers for the Semantic Graph
enum class EdgeWeight : uint32_t {
    CasePair = 100,
    CanonicalDecomp = 95,
    UCAPrimary = 90,
    UCASecondary = 85,
    Confusable = 80,
    ScriptAdjacency = 70,
    RadicalStroke = 65,
    EmojiZWJ = 60,
    NumericAdjacency = 50,
    BlockAdjacency = 40,
    CompatibilityDecomp = 30,
    Default = 1
};

} // namespace Hartonomous::unicode
