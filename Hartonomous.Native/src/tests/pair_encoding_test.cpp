/// CPE Cascade Tests
/// Demonstrates the Content Pair Encoding algorithm on sample strings.

#include "../atoms/pair_encoding_cascade.hpp"
#include <cstdio>
#include <string>

using namespace hartonomous;

void print_node(NodeRef ref, int depth = 0) {
    for (int i = 0; i < depth; ++i) printf("  ");

    if (ref.is_atom) {
        printf("[ATOM %llX:%llX]\n",
               static_cast<unsigned long long>(ref.id_high),
               static_cast<unsigned long long>(ref.id_low));
    } else {
        printf("[COMP %llX:%llX]\n",
               static_cast<unsigned long long>(ref.id_high),
               static_cast<unsigned long long>(ref.id_low));
    }
}

void test_string(const char* str) {
    printf("=== Testing: \"%s\" ===\n", str);

    GlobalCompositionStore store;
    NodeRef root = PairEncodingCascade::encode(str, store);

    printf("Root: %s %llX:%llX\n",
           root.is_atom ? "ATOM" : "COMP",
           static_cast<unsigned long long>(root.id_high),
           static_cast<unsigned long long>(root.id_low));

    printf("Compositions created: %zu\n", store.size());
    printf("\n");
}

void test_shared_patterns() {
    printf("=== Testing shared patterns across strings ===\n");

    GlobalCompositionStore shared_store;

    const char* strings[] = {
        "banana",
        "ananas",
        "canal",
        "the cat",
        "the dog",
        "the hat",
    };

    for (const char* str : strings) {
        printf("\"%s\":\n", str);
        NodeRef root = PairEncodingCascade::encode(str, shared_store);
        printf("  Root: %llX:%llX\n",
               static_cast<unsigned long long>(root.id_high),
               static_cast<unsigned long long>(root.id_low));
        printf("  Total compositions so far: %zu\n", shared_store.size());
    }

    printf("\nTotal unique compositions: %zu\n", shared_store.size());
    printf("(Shared patterns are deduplicated via Merkle hash)\n\n");
}

void test_rle() {
    printf("=== Testing RLE ===\n");

    {
        GlobalCompositionStore store;
        const char* aaa = "aaa";
        printf("\"%s\":\n", aaa);
        NodeRef root = PairEncodingCascade::encode(aaa, store);
        printf("  Root: %s %llX:%llX\n",
               root.is_atom ? "ATOM" : "COMP",
               static_cast<unsigned long long>(root.id_high),
               static_cast<unsigned long long>(root.id_low));
        printf("  Compositions: %zu\n", store.size());
    }

    {
        GlobalCompositionStore store;
        const char* aaabbb = "aaabbb";
        printf("\"%s\":\n", aaabbb);
        NodeRef root = PairEncodingCascade::encode(aaabbb, store);
        printf("  Root: %s %llX:%llX\n",
               root.is_atom ? "ATOM" : "COMP",
               static_cast<unsigned long long>(root.id_high),
               static_cast<unsigned long long>(root.id_low));
        printf("  Compositions: %zu\n", store.size());
    }

    printf("\n");
}

int main() {
    printf("CPE Cascade Algorithm Tests\n");
    printf("============================\n\n");

    // User's examples
    test_string("banana");
    test_string("Mississippi");
    test_string("Tennessee");

    // RLE tests
    test_rle();

    // Shared pattern test
    test_shared_patterns();

    printf("=== Done ===\n");
    return 0;
}
