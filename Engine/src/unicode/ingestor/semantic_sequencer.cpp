#include <unicode/ingestor/semantic_sequencer.hpp>
#include <algorithm>
#include <iostream>
#include <string>

namespace Hartonomous::unicode {

// Helper to parse hex strings from UCD (e.g., "0041" or "0041 0300")
static uint32_t parse_first_codepoint(const std::string& s) {
    if (s.empty() || s == "#") return 0;
    try {
        size_t end_pos;
        unsigned long val = std::stoul(s, &end_pos, 16);
        return static_cast<uint32_t>(val);
    } catch (...) {
        return 0;
    }
}

void SemanticSequencer::build_graph(std::map<uint32_t, CodepointMetadata>& codepoints) {
    std::cout << "Building semantic adjacency graph (UCA-weighted)..." << std::endl;
    for (auto& [cp, meta] : codepoints) {
        // 1. Case Adjacency
        // simple_uppercase/lowercase are strings in CodepointMetadata
        uint32_t upper = parse_first_codepoint(meta.simple_uppercase);
        if (upper != 0 && upper != cp) {
            add_edge(meta, upper, EdgeWeight::CasePair, "case_upper");
        }

        uint32_t lower = parse_first_codepoint(meta.simple_lowercase);
        if (lower != 0 && lower != cp) {
            add_edge(meta, lower, EdgeWeight::CasePair, "case_lower");
        }

        // 2. Decomposition Adjacency
        if (meta.base_codepoint != 0 && meta.base_codepoint != cp) {
            add_edge(meta, meta.base_codepoint, EdgeWeight::CanonicalDecomp, "decomposition");
        }
        
        // 3. Han Radical Adjacency (optional, but good for completeness)
        // (meta.radical is already a uint32_t)
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
            if (c == 'L') meta.primary_group = 1;      // Letters
            else if (c == 'N') meta.primary_group = 2; // Numbers
            else if (c == 'P') meta.primary_group = 3; // Punctuation
            else if (c == 'S') meta.primary_group = 4; // Symbols
            else if (c == 'M') meta.primary_group = 5; // Marks
            else if (c == 'Z') meta.primary_group = 6; // Separators
            else meta.primary_group = 7;               // Other
        }

        meta.script_group = get_script_id(meta.script);
        sorted.push_back(&meta);
    }

    // Vision-Driven Sort: "A near a"
    std::sort(sorted.begin(), sorted.end(), [](const CodepointMetadata* a, const CodepointMetadata* b) {
        // 1. Primary Group (Letters, Numbers, etc.)
        if (a->primary_group != b->primary_group) return a->primary_group < b->primary_group;

        // 2. Script
        if (a->script_group != b->script_group) return a->script_group < b->script_group;

        // 3. UCA Primary Weight (This is the key!)
        uint32_t wa = a->uca_elements.empty() ? 0 : a->uca_elements[0].primary;
        uint32_t wb = b->uca_elements.empty() ? 0 : b->uca_elements[0].primary;
        if (wa != wb) return wa < wb;

        // 4. UCA Secondary Weight
        uint32_t sa = a->uca_elements.empty() ? 0 : a->uca_elements[0].secondary;
        uint32_t sb = b->uca_elements.empty() ? 0 : b->uca_elements[0].secondary;
        if (sa != sb) return sa < sb;

        // 5. Han Radical
        if (a->radical != b->radical) return a->radical < b->radical;
        if (a->strokes != b->strokes) return a->strokes < b->strokes;

        // 6. Base Codepoint
        if (a->base_codepoint != b->base_codepoint) return a->base_codepoint < b->base_codepoint;

        // 7. Fallback
        return a->codepoint < b->codepoint;
    });

    return sorted;
}

} // namespace Hartonomous::unicode