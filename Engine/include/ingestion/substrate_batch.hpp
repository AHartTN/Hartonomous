#pragma once

#include <storage/physicality_store.hpp>
#include <storage/composition_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <vector>

namespace Hartonomous {

/**
 * @brief Unified container for substrate records to be enqueued for bulk flush.
 */
struct SubstrateBatch {
    std::vector<PhysicalityRecord> phys;
    std::vector<CompositionRecord> comp;
    std::vector<CompositionSequenceRecord> seq;
    std::vector<RelationRecord> rel;
    std::vector<RelationSequenceRecord> rel_seq;
    std::vector<RelationRatingRecord> rating;
    std::vector<RelationEvidenceRecord> evidence;

    void clear() {
        phys.clear(); comp.clear(); seq.clear();
        rel.clear(); rel_seq.clear(); rating.clear(); evidence.clear();
    }

    bool empty() const {
        return phys.empty() && comp.empty() && rel.empty() && evidence.empty();
    }

    size_t record_count() const {
        return phys.size() + comp.size() + seq.size() +
               rel.size() + rel_seq.size() + rating.size() + evidence.size();
    }
};

} // namespace Hartonomous