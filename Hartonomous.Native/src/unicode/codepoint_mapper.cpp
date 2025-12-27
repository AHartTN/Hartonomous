#include "codepoint_mapper.hpp"

namespace hartonomous {

// Compile-time verification of the complete mapping pipeline
// CENTER-ORIGIN: coordinates are SIGNED, origin at (0,0,0,0)

// Basic Latin 'A' (U+0041)
static_assert([] {
    auto mapping = CodepointMapper::map(0x0041);
    return mapping.is_valid() && 
           mapping.codepoint == 0x0041 &&
           mapping.block_name == "Basic Latin" &&
           mapping.surface_point.face == TesseractFace::XNeg;
}(), "CodepointMapper: 'A' mapping failed");

// Greek lowercase alpha (U+03B1)
static_assert([] {
    auto mapping = CodepointMapper::map(0x03B1);
    return mapping.is_valid() && 
           mapping.block_name == "Greek and Coptic" &&
           mapping.surface_point.face == TesseractFace::XPos;
}(), "CodepointMapper: Greek alpha mapping failed");

// CJK ideograph for 'one' (U+4E00)
static_assert([] {
    auto mapping = CodepointMapper::map(0x4E00);
    return mapping.is_valid() && 
           mapping.block_name == "CJK Unified Ideographs" &&
           mapping.surface_point.face == TesseractFace::YNeg;
}(), "CodepointMapper: CJK ideograph mapping failed");

// Grinning face emoji (U+1F600)
static_assert([] {
    auto mapping = CodepointMapper::map(0x1F600);
    return mapping.is_valid() && 
           mapping.block_name == "Emoticons" &&
           mapping.surface_point.face == TesseractFace::WPos;
}(), "CodepointMapper: Emoji mapping failed");

// Surrogate values are not valid scalars
static_assert(!CodepointMapper::is_valid_scalar(0xD800), "Surrogate should not be valid scalar");
static_assert(!CodepointMapper::is_valid_scalar(0xDFFF), "Surrogate should not be valid scalar");

// Uniqueness: different codepoints must have different Hilbert indices
static_assert([] {
    auto idx_A = CodepointMapper::get_hilbert_index(0x0041);
    auto idx_B = CodepointMapper::get_hilbert_index(0x0042);
    auto idx_C = CodepointMapper::get_hilbert_index(0x0043);
    return idx_A != idx_B && idx_B != idx_C && idx_A != idx_C;
}(), "Hilbert indices must be unique for different codepoints");

} // namespace hartonomous
