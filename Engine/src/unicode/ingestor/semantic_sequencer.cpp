#include <unicode/ingestor/semantic_sequencer.hpp>
#include <algorithm>
#include <iostream>

namespace Hartonomous::unicode {

void SemanticSequencer::build_graph(std::map<uint32_t, CodepointMetadata>& codepoints) {
    std::cout << "Building semantic adjacency graph...\n";
    for (auto& [cp, meta] : codepoints) {
        // 1. Decomposition Adjacency
        if (meta.base_codepoint != meta.codepoint) {
            add_edge(meta, meta.base_codepoint, EdgeWeight::CanonicalDecomp, "decomposition");
        }

        // 2. Han Radical Adjacency
        // (Optional: add edges for same radical)
    }
}

void SemanticSequencer::add_edge(CodepointMetadata& meta, uint32_t target, EdgeWeight weight, const std::string& type) {
    meta.edges.push_back({target, static_cast<uint32_t>(weight), type});
}

uint32_t SemanticSequencer::get_script_id(const std::string& script) {
    if (script.empty()) return 999;
    if (script_map_.find(script) == script_map_.end()) {
        script_map_[script] = static_cast<uint32_t>(script_map_.size());
    }
    return script_map_[script];
}

std::vector<CodepointMetadata*> SemanticSequencer::linearize(std::map<uint32_t, CodepointMetadata>& codepoints) {
    std::vector<CodepointMetadata*> sorted;
    sorted.reserve(codepoints.size());

    for (auto& [cp, meta] : codepoints) {
        // Group assignment
        std::string gc = meta.general_category;
        if (gc.empty()) meta.primary_group = 7;
        else {
            char c = gc[0];
            if (c == 'L') meta.primary_group = 1;
            else if (c == 'N') meta.primary_group = 2;
            else if (c == 'P') meta.primary_group = 3;
            else if (c == 'S') meta.primary_group = 4;
            else if (c == 'M') meta.primary_group = 5;
            else if (c == 'Z') meta.primary_group = 6;
            else meta.primary_group = 7;
        }

        meta.script_group = get_script_id(meta.script);
        sorted.push_back(&meta);
    }

    // Deterministic sort based on vision
    std::sort(sorted.begin(), sorted.end(), [](const CodepointMetadata* a, const CodepointMetadata* b) {
        // 1. Primary Group (Letters, Numbers, etc.)
        if (a->primary_group != b->primary_group) return a->primary_group < b->primary_group;

        // 2. Script
        if (a->script_group != b->script_group) return a->script_group < b->script_group;

        // 3. UCA Weight (if available)
        uint32_t wa = a->uca_elements.empty() ? 0 : a->uca_elements[0].primary;
        uint32_t wb = b->uca_elements.empty() ? 0 : b->uca_elements[0].primary;
        if (wa != wb) return wa < wb;

        // 4. Han Radical/Stroke
        if (a->radical != b->radical) return a->radical < b->radical;
        if (a->strokes != b->strokes) return a->strokes < b->strokes;

        // 5. Base Codepoint (cluster variants)
        if (a->base_codepoint != b->base_codepoint) return a->base_codepoint < b->base_codepoint;

        // 6. Fallback
        return a->codepoint < b->codepoint;
    });

    return sorted;
}

} // namespace Hartonomous::unicode
