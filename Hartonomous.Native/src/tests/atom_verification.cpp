/// Atom ID Verification Tests
/// These tests produce deterministic values that can be verified in Excel.
/// All math is integer-only for exact reproducibility.

#include "../atoms/semantic_decompose.hpp"
#include <cstdio>
#include <cassert>

using namespace hartonomous;

// Helper to print AtomId in hex for Excel verification
void print_atom(const char* name, std::int32_t codepoint) {
    auto coord = SemanticDecompose::decompose(codepoint);
    auto id = SemanticDecompose::get_atom_id(codepoint);

    printf("%s (U+%04X):\n", name, codepoint);
    printf("  Semantic: page=%u, type=%u, base=0x%X, variant=%u\n",
           coord.page, coord.type, coord.base, coord.variant);
    printf("  Packed:   0x%08X\n", coord.pack());
    printf("  AtomId:   high=0x%016llX, low=0x%016llX\n",
           (unsigned long long)id.high, (unsigned long long)id.low);
    printf("\n");
}

// Compile-time verification of core properties
namespace static_tests {

// Basic decomposition
static_assert(SemanticDecompose::decompose('A').page == 0);  // Latin
static_assert(SemanticDecompose::decompose('A').type == 1);  // Letter
static_assert(SemanticDecompose::decompose('A').base == 'a'); // Lowercase base
static_assert(SemanticDecompose::decompose('A').variant == 1); // Uppercase variant

static_assert(SemanticDecompose::decompose('a').base == 'a');
static_assert(SemanticDecompose::decompose('a').variant == 0);

// Case clustering: A and a should have same base
static_assert(SemanticDecompose::decompose('A').base == SemanticDecompose::decompose('a').base);

// Diacritical clustering: Ä and A should have same base
static_assert(SemanticDecompose::decompose(0x00C4).base == 'a');  // Ä → a
static_assert(SemanticDecompose::decompose(0x00E4).base == 'a');  // ä → a

// Type classification
static_assert(SemanticDecompose::decompose('0').type == 2);  // Number
static_assert(SemanticDecompose::decompose('.').type == 3);  // Punctuation
static_assert(SemanticDecompose::decompose('+').type == 3);  // Punctuation (in ASCII range)

// Page assignment
static_assert(SemanticDecompose::decompose(0x0391).page == 1);  // Greek Α → European
static_assert(SemanticDecompose::decompose(0x0410).page == 1);  // Cyrillic А → European
static_assert(SemanticDecompose::decompose(0x4E00).page == 2);  // CJK → CJK_Common
static_assert(SemanticDecompose::decompose(0x1F600).page == 7); // Emoji → Supplementary

// Hilbert encoding is bijective (round-trip)
constexpr bool test_roundtrip(std::int32_t cp) {
    auto id = SemanticDecompose::get_atom_id(cp);
    auto coords = HilbertEncoder::decode(id);
    auto id2 = HilbertEncoder::encode(coords);
    return id.high == id2.high && id.low == id2.low;
}

static_assert(test_roundtrip('A'));
static_assert(test_roundtrip('z'));
static_assert(test_roundtrip('0'));
static_assert(test_roundtrip(0x4E00));
static_assert(test_roundtrip(0x1F600));

// Ordering: Related characters should be "nearby" (same page+type)
constexpr bool same_region(std::int32_t cp1, std::int32_t cp2) {
    auto c1 = SemanticDecompose::decompose(cp1);
    auto c2 = SemanticDecompose::decompose(cp2);
    return c1.page == c2.page && c1.type == c2.type;
}

static_assert(same_region('A', 'B'));   // Adjacent letters
static_assert(same_region('A', 'a'));   // Case variants
static_assert(same_region('0', '9'));   // Digits
static_assert(!same_region('A', '0'));  // Letter vs number

} // namespace static_tests

int main() {
    printf("=== Atom ID Verification ===\n");
    printf("All values are deterministic and can be verified in Excel.\n\n");

    // Basic Latin
    printf("--- BASIC LATIN ---\n");
    print_atom("A", 'A');
    print_atom("a", 'a');
    print_atom("B", 'B');
    print_atom("Z", 'Z');
    print_atom("z", 'z');

    // Digits
    printf("--- DIGITS ---\n");
    print_atom("0", '0');
    print_atom("1", '1');
    print_atom("9", '9');

    // Diacriticals
    printf("--- DIACRITICALS ---\n");
    print_atom("Ä", 0x00C4);
    print_atom("ä", 0x00E4);
    print_atom("é", 0x00E9);
    print_atom("ñ", 0x00F1);

    // Greek
    printf("--- GREEK ---\n");
    print_atom("Α (Alpha)", 0x0391);
    print_atom("α (alpha)", 0x03B1);
    print_atom("Ω (Omega)", 0x03A9);

    // Cyrillic
    printf("--- CYRILLIC ---\n");
    print_atom("А (Cyrillic A)", 0x0410);
    print_atom("а (Cyrillic a)", 0x0430);

    // CJK
    printf("--- CJK ---\n");
    print_atom("一 (one)", 0x4E00);
    print_atom("二 (two)", 0x4E8C);
    print_atom("中 (middle)", 0x4E2D);

    // Symbols
    printf("--- SYMBOLS ---\n");
    print_atom("+ (plus)", '+');
    print_atom("= (equals)", '=');
    print_atom("→ (arrow)", 0x2192);

    // Emoji
    printf("--- EMOJI ---\n");
    print_atom("😀 (grin)", 0x1F600);
    print_atom("😁 (beam)", 0x1F601);

    // Punctuation
    printf("--- PUNCTUATION ---\n");
    print_atom(". (period)", '.');
    print_atom(", (comma)", ',');
    print_atom("! (exclaim)", '!');
    print_atom("? (question)", '?');

    // Control/System
    printf("--- CONTROL ---\n");
    print_atom("NUL", 0x0000);
    print_atom("TAB", 0x0009);
    print_atom("LF", 0x000A);
    print_atom("SPACE", 0x0020);

    printf("=== All static assertions passed! ===\n");
    printf("=== Verify hex values match in Excel ===\n");

    return 0;
}
