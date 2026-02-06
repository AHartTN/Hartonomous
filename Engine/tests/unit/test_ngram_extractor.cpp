/**
 * @file test_ngram_extractor.cpp
 * @brief Unit tests for suffix array-based composition discovery
 *
 * Tests the NGramExtractor: SA construction, frequency counting,
 * composition discovery, pattern signatures, and position tracking.
 * No database needed — pure in-memory logic.
 */

#include <gtest/gtest.h>
#include <ingestion/ngram_extractor.hpp>
#include <string>
#include <unordered_set>
#include <algorithm>
#include <chrono>

using namespace Hartonomous;

// Helper: convert ASCII to u32string
static std::u32string to_u32(const std::string& s) {
    return std::u32string(s.begin(), s.end());
}

// Helper: find ngram by text content
static const NGram* find_ngram(const NGramExtractor& ex, const std::u32string& text) {
    for (const auto& [hash, ng] : ex.ngrams()) {
        if (ng.text == text) return &ng;
    }
    return nullptr;
}

// ============================================================================
// Basic extraction
// ============================================================================

TEST(NGramExtractorTest, EmptyInput) {
    NGramExtractor ex;
    ex.extract(U"");
    EXPECT_EQ(ex.total_ngrams(), 0u);
}

TEST(NGramExtractorTest, SingleCodepoint) {
    NGramExtractor ex;
    ex.extract(U"a");
    EXPECT_GE(ex.total_ngrams(), 1u);
    auto* ng = find_ngram(ex, U"a");
    ASSERT_NE(ng, nullptr);
    EXPECT_EQ(ng->frequency, 1u);
    EXPECT_EQ(ng->n, 1u);
}

TEST(NGramExtractorTest, UniformText) {
    // "aaaa" — should find 'a' with freq 4, "aa" with freq 3, "aaa" with freq 2
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    ex.extract(U"aaaa");

    auto* a = find_ngram(ex, U"a");
    ASSERT_NE(a, nullptr);
    EXPECT_EQ(a->frequency, 4u);

    auto* aa = find_ngram(ex, U"aa");
    ASSERT_NE(aa, nullptr);
    EXPECT_EQ(aa->frequency, 3u);
    EXPECT_TRUE(aa->is_rle); // repeated atom

    auto* aaa = find_ngram(ex, U"aaa");
    ASSERT_NE(aaa, nullptr);
    EXPECT_EQ(aaa->frequency, 2u);
    EXPECT_TRUE(aaa->is_rle);
}

// ============================================================================
// Frequency counting accuracy
// ============================================================================

TEST(NGramExtractorTest, RepeatedSubstring) {
    // "abcabc" — "abc" appears 2x, "ab" appears 2x, "bc" appears 2x
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    ex.extract(to_u32("abcabc"));

    auto* abc = find_ngram(ex, to_u32("abc"));
    ASSERT_NE(abc, nullptr);
    EXPECT_EQ(abc->frequency, 2u);

    auto* ab = find_ngram(ex, to_u32("ab"));
    ASSERT_NE(ab, nullptr);
    EXPECT_EQ(ab->frequency, 2u);
}

TEST(NGramExtractorTest, Mississippi) {
    // Classic test case
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    ex.extract(to_u32("mississippi"));

    // 'i' appears 4x, 's' appears 4x, 'p' appears 2x
    auto* i = find_ngram(ex, to_u32("i"));
    ASSERT_NE(i, nullptr);
    EXPECT_EQ(i->frequency, 4u);

    auto* s = find_ngram(ex, to_u32("s"));
    ASSERT_NE(s, nullptr);
    EXPECT_EQ(s->frequency, 4u);

    // "ss" appears 2x, "issi" appears 2x
    auto* ss = find_ngram(ex, to_u32("ss"));
    ASSERT_NE(ss, nullptr);
    EXPECT_EQ(ss->frequency, 2u);

    auto* issi = find_ngram(ex, to_u32("issi"));
    ASSERT_NE(issi, nullptr);
    EXPECT_EQ(issi->frequency, 2u);
}

TEST(NGramExtractorTest, NoLengthLimit) {
    // Long repeated substring should be found even beyond old max_n=8
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    std::string long_word = "telephony";
    std::string input = long_word + "___" + long_word; // appears 2x
    ex.extract(to_u32(input));

    auto* t = find_ngram(ex, to_u32(long_word));
    ASSERT_NE(t, nullptr) << "Should find 'telephony' (9 chars) without length limit";
    EXPECT_EQ(t->frequency, 2u);
    EXPECT_EQ(t->n, 9u);
}

// ============================================================================
// Position tracking
// ============================================================================

TEST(NGramExtractorTest, PositionsAreSorted) {
    NGramConfig config;
    config.min_frequency = 2;
    config.track_positions = true;
    NGramExtractor ex(config);
    ex.extract(to_u32("abab"));

    auto* ab = find_ngram(ex, to_u32("ab"));
    ASSERT_NE(ab, nullptr);
    EXPECT_EQ(ab->positions.size(), 2u);
    EXPECT_TRUE(std::is_sorted(ab->positions.begin(), ab->positions.end()));
    EXPECT_EQ(ab->positions[0], 0u);
    EXPECT_EQ(ab->positions[1], 2u);
}

// ============================================================================
// Pattern signatures
// ============================================================================

TEST(NGramExtractorTest, PatternSignatureXXY) {
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    // "ssi" in "ssissi" appears 2x → pattern = XXY
    ex.extract(to_u32("ssissi"));

    auto* ssi = find_ngram(ex, to_u32("ssi"));
    ASSERT_NE(ssi, nullptr);
    EXPECT_EQ(ssi->pattern_signature, "XXY");
}

TEST(NGramExtractorTest, PatternSignatureXYYX) {
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    // "abba" repeated
    ex.extract(to_u32("abbaabba"));

    auto* abba = find_ngram(ex, to_u32("abba"));
    ASSERT_NE(abba, nullptr);
    EXPECT_EQ(abba->pattern_signature, "XYYX");
}

TEST(NGramExtractorTest, PatternSignatureXYXY) {
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    ex.extract(to_u32("abababab"));

    auto* abab = find_ngram(ex, to_u32("abab"));
    ASSERT_NE(abab, nullptr);
    EXPECT_EQ(abab->pattern_signature, "XYXY");
}

// ============================================================================
// RLE detection
// ============================================================================

TEST(NGramExtractorTest, RLEDetection) {
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    ex.extract(to_u32("aaaaabbbbb"));

    auto* aa = find_ngram(ex, to_u32("aa"));
    ASSERT_NE(aa, nullptr);
    EXPECT_TRUE(aa->is_rle);

    auto* ab = find_ngram(ex, to_u32("ab"));
    // "ab" only appears once, won't be stored at min_freq=2
    // But if found, it shouldn't be RLE
}

// ============================================================================
// Significant ngrams filtering
// ============================================================================

TEST(NGramExtractorTest, SignificantIncludesAllUnigrams) {
    NGramConfig config;
    config.min_frequency = 100; // Very high threshold
    NGramExtractor ex(config);
    ex.extract(to_u32("abc"));

    auto sig = ex.significant_ngrams();
    // All unigrams should be included regardless of min_frequency
    EXPECT_GE(sig.size(), 3u);
    
    bool found_a = false, found_b = false, found_c = false;
    for (const auto* ng : sig) {
        if (ng->text == U"a") found_a = true;
        if (ng->text == U"b") found_b = true;
        if (ng->text == U"c") found_c = true;
    }
    EXPECT_TRUE(found_a);
    EXPECT_TRUE(found_b);
    EXPECT_TRUE(found_c);
}

// ============================================================================
// Unicode support
// ============================================================================

TEST(NGramExtractorTest, UnicodeCodepoints) {
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    // Japanese: 日本語 repeated
    std::u32string text = U"日本語日本語";
    ex.extract(text);

    auto* nihon = find_ngram(ex, U"日本");
    ASSERT_NE(nihon, nullptr);
    EXPECT_EQ(nihon->frequency, 2u);
}

TEST(NGramExtractorTest, EmojiComposition) {
    NGramConfig config;
    config.min_frequency = 2;
    NGramExtractor ex(config);
    std::u32string text = U"😀😂😀😂";
    ex.extract(text);

    auto* pair = find_ngram(ex, U"😀😂");
    ASSERT_NE(pair, nullptr);
    EXPECT_EQ(pair->frequency, 2u);
}

// ============================================================================
// Performance sanity check
// ============================================================================

TEST(NGramExtractorTest, PerformanceSanity) {
    // Generate 100K codepoints of repeated text
    std::u32string text;
    std::u32string pattern = to_u32("the quick brown fox ");
    while (text.size() < 100000) text += pattern;

    NGramConfig config;
    config.min_frequency = 5;
    NGramExtractor ex(config);

    auto start = std::chrono::steady_clock::now();
    ex.extract(text);
    auto elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();

    // Should complete in under 10 seconds for 100K codepoints
    EXPECT_LT(elapsed, 10.0) << "Extraction took " << elapsed << "s for 100K codepoints";
    EXPECT_GT(ex.total_ngrams(), 10u); // Should find compositions

    // "the" should be very frequent
    auto* the = find_ngram(ex, to_u32("the"));
    ASSERT_NE(the, nullptr);
    EXPECT_GT(the->frequency, 1000u);
}

// ============================================================================
// BLAKE3 hash determinism
// ============================================================================

TEST(NGramExtractorTest, HashDeterminism) {
    // Same input should produce same hashes
    NGramConfig config;
    config.min_frequency = 2;
    
    NGramExtractor ex1(config);
    ex1.extract(to_u32("hello world hello world"));
    
    NGramExtractor ex2(config);
    ex2.extract(to_u32("hello world hello world"));

    auto* hw1 = find_ngram(ex1, to_u32("hello"));
    auto* hw2 = find_ngram(ex2, to_u32("hello"));
    ASSERT_NE(hw1, nullptr);
    ASSERT_NE(hw2, nullptr);
    EXPECT_EQ(hw1->hash, hw2->hash);
}
