#include "unicode_blocks.hpp"

namespace hartonomous {

// Compile-time verification of block lookups

static_assert(get_block_name('A') == "Basic Latin", "Block lookup for 'A' failed");
static_assert(get_block_name(0x03B1) == "Greek and Coptic", "Block lookup for alpha failed");
static_assert(get_block_name(0x4E00) == "CJK Unified Ideographs", "Block lookup for CJK failed");
static_assert(get_block_name(0x1F600) == "Emoticons", "Block lookup for emoji failed");

} // namespace hartonomous
