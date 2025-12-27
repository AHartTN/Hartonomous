#include "../geometry/tesseract_surface.hpp"
#include "../unicode/semantic_ordering.hpp"
#include <cstdio>
#include <cmath>

using namespace hartonomous;

// Test helper: print distance info between two codepoints
void print_distance(std::int32_t cp1, std::int32_t cp2, const char* desc1, const char* desc2) {
    double euclidean = std::sqrt(TesseractSurface::euclidean_distance_squared(cp1, cp2));
    std::uint64_t semantic = TesseractSurface::semantic_distance(cp1, cp2);
    bool related = TesseractSurface::are_related(cp1, cp2);

    auto p1 = TesseractSurface::map_codepoint(cp1);
    auto p2 = TesseractSurface::map_codepoint(cp2);

    printf("%-8s (U+%04X) <-> %-8s (U+%04X)\n", desc1, cp1, desc2, cp2);
    printf("  Euclidean distance: %.6f\n", euclidean);
    printf("  Semantic distance:  %llu\n", (unsigned long long)semantic);
    printf("  Related (same base): %s\n", related ? "YES" : "no");
    printf("  Faces: %d, %d\n", static_cast<int>(p1.face), static_cast<int>(p2.face));
    printf("\n");
}

// Compile-time verification that related characters cluster
static_assert([] {
    // 'A' and 'a' should be related (same base)
    return SemanticOrdering::are_related(0x0041, 0x0061);
}(), "'A' and 'a' must be related");

static_assert([] {
    // 'A' and 'Ä' should be related
    return SemanticOrdering::are_related(0x0041, 0x00C4);
}(), "'A' and 'Ä' must be related");

static_assert([] {
    // 'a' and 'ä' should be related
    return SemanticOrdering::are_related(0x0061, 0x00E4);
}(), "'a' and 'ä' must be related");

static_assert([] {
    // 'A' and 'B' should NOT be related (different base)
    return !SemanticOrdering::are_related(0x0041, 0x0042);
}(), "'A' and 'B' must NOT be related");

static_assert([] {
    // Related characters should have smaller semantic distance than unrelated
    auto dist_A_a = SemanticOrdering::semantic_distance(0x0041, 0x0061);
    auto dist_A_B = SemanticOrdering::semantic_distance(0x0041, 0x0042);
    return dist_A_a < dist_A_B;
}(), "'A'-'a' distance must be less than 'A'-'B' distance");

static_assert([] {
    // Same face for Basic Latin
    auto face_A = SemanticOrdering::get_semantic_face(0x0041);
    auto face_a = SemanticOrdering::get_semantic_face(0x0061);
    return face_A == face_a;
}(), "'A' and 'a' must be on same face");

static_assert([] {
    // CJK on different face from Latin
    auto face_latin = SemanticOrdering::get_semantic_face(0x0041);
    auto face_cjk = SemanticOrdering::get_semantic_face(0x4E00);
    return face_latin != face_cjk;
}(), "Latin and CJK must be on different faces");

int main() {
    printf("=== Unicode Tesseract Distance Tests ===\n\n");

    printf("--- Case variants (should be very close) ---\n");
    print_distance('A', 'a', "A", "a");
    print_distance('B', 'b', "B", "b");
    print_distance('Z', 'z', "Z", "z");

    printf("--- Diacritical variants (should be close) ---\n");
    print_distance('A', 0x00C4, "A", "Ä");  // A and A-umlaut
    print_distance('a', 0x00E4, "a", "ä");  // a and a-umlaut
    print_distance('A', 0x00C0, "A", "À");  // A and A-grave
    print_distance('e', 0x00E9, "e", "é");  // e and e-acute
    print_distance('o', 0x00F6, "o", "ö");  // o and o-umlaut

    printf("--- Adjacent letters (should be moderate distance) ---\n");
    print_distance('A', 'B', "A", "B");
    print_distance('M', 'N', "M", "N");
    print_distance('a', 'b', "a", "b");

    printf("--- Different categories (should be far) ---\n");
    print_distance('A', '1', "A", "1");
    print_distance('a', '.', "a", ".");
    print_distance('0', '!', "0", "!");

    printf("--- Different scripts (should be very far) ---\n");
    print_distance('A', 0x03B1, "A", "α");      // Latin vs Greek
    print_distance('A', 0x0410, "A", "А");      // Latin A vs Cyrillic А
    print_distance('a', 0x4E00, "a", "一");     // Latin vs CJK
    print_distance('1', 0x0661, "1", "١");      // Western vs Arabic-Indic digit

    printf("--- Numbers (should cluster) ---\n");
    print_distance('0', '1', "0", "1");
    print_distance('1', '9', "1", "9");
    print_distance('0', '9', "0", "9");

    printf("--- Punctuation (should cluster) ---\n");
    print_distance('.', ',', ".", ",");
    print_distance('!', '?', "!", "?");
    print_distance('.', ';', ".", ";");

    printf("--- CJK proximity (should cluster within CJK) ---\n");
    print_distance(0x4E00, 0x4E01, "一", "丁");  // First two CJK
    print_distance(0x4E00, 0x4E09, "一", "三");  // One and Three
    print_distance(0x4E00, 0x5341, "一", "十");  // One and Ten

    printf("--- Greek (should cluster) ---\n");
    print_distance(0x0391, 0x03B1, "Α", "α");   // Alpha upper/lower
    print_distance(0x0392, 0x03B2, "Β", "β");   // Beta upper/lower
    print_distance(0x0391, 0x0392, "Α", "Β");   // Alpha vs Beta

    printf("--- Emoji (supplementary plane) ---\n");
    print_distance(0x1F600, 0x1F601, "😀", "😁");  // Adjacent emoji
    print_distance(0x1F600, 0x1F60A, "😀", "😊");  // Similar emoji
    print_distance(0x1F600, 0x0041, "😀", "A");    // Emoji vs Latin

    printf("\n=== All compile-time assertions passed! ===\n");

    return 0;
}
