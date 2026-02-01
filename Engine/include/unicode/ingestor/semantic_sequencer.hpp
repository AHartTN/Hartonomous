#pragma once

#include "ucd_models.hpp"
#include <map>
#include <vector>

namespace Hartonomous::unicode {

class SemanticSequencer {
public:
    /**
     * @brief Build the adjacency graph based on UCD metadata
     */
    void build_graph(std::vector<CodepointMetadata>& codepoints);

    /**
     * @brief Linearize the graph into a total order
     * 
     * Uses a multi-level sort followed by a deterministic graph traversal
     * to break ties and preserve semantic locality.
     */
    std::vector<CodepointMetadata*> linearize(std::vector<CodepointMetadata>& codepoints);

private:
    void add_edge(CodepointMetadata& meta, uint32_t target, EdgeWeight weight, const std::string& type);
    uint32_t get_script_id(const std::string& script);
    
    std::map<std::string, uint32_t> script_map_;
};

} // namespace Hartonomous::unicode
