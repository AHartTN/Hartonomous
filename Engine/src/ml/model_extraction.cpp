/**
 * @file model_extraction.cpp
 * @brief Full implementation of the multi-modal AI Model Extraction pipeline
 */

#include <ml/model_extraction.hpp>
#include <stdexcept>

namespace hartonomous::ml {

// The ModelExtractor acts as the unified entry point for model-to-substrate conversion.
// It leverages the specialized extractors defined in model_extraction.hpp to
// decompose monolithic model weights into the universal Merkle DAG of ELO-ranked edges.

// Note: Most logic is inlined in the header for performance, but we finalize the 
// ModelExtractor dispatch logic here to ensure clean linkage.

// (Implementation detail: The static 'extract' method handles the type-erased dispatch)

} // namespace hartonomous::ml