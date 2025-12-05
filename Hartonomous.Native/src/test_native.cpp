#include <gtest/gtest.h>
#include <vector>
#include <random>
#include "skilling.h"

// Prototypes for functions to test
void InterleaveBits4D(uint32_t* X, int b, uint64_t* high, uint64_t* low);
void DeinterleaveBits4D(uint64_t high, uint64_t low, int b, uint32_t* X);

void InterleaveBits4D(uint32_t* X, int b, uint64_t* high, uint64_t* low) {
    *high = 0;
    *low = 0;
    int n = 4;

    for (int i = b - 1; i >= 0; i--) {
        for (int j = 0; j < n; j++) {
            uint64_t bit = (X[j] >> i) & 1;
            int pos = i * n + j; // 0 is LSB
            
            if (pos >= 42) {
                *high |= (bit << (pos - 42));
            } else {
                *low |= (bit << pos);
            }
        }
    }
}

void DeinterleaveBits4D(uint64_t high, uint64_t low, int b, uint32_t* X) {
    int n = 4;
    for (int j = 0; j < n; j++) X[j] = 0;

    for (int i = 0; i < b; i++) {
        for (int j = 0; j < n; j++) {
            int pos = i * n + j;
            uint32_t bit;
            
            if (pos >= 42) {
                bit = (high >> (pos - 42)) & 1;
            } else {
                bit = (low >> pos) & 1;
            }
            
            X[j] |= (bit << i);
        }
    }
}

extern "C" void HilbertEncode4D(uint32_t x, uint32_t y, uint32_t z, uint32_t m, int precision, uint64_t* resultHigh, uint64_t* resultLow);
extern "C" void HilbertDecode4D(uint64_t indexHigh, uint64_t indexLow, int precision, uint32_t* resultCoords);

// Google Test fixtures and tests
class HilbertCurveTest : public ::testing::Test {
protected:
    static constexpr int B = 21;
    static constexpr int N = 4;
    static constexpr uint32_t MAX_VAL = (1 << B) - 1;
};

TEST_F(HilbertCurveTest, DiagnosticKnownValues) {
    uint32_t x = 100, y = 200, z = 300, m = 400;
    uint64_t high, low;
    HilbertEncode4D(x, y, z, m, B, &high, &low);
    
    uint32_t decoded[N];
    HilbertDecode4D(high, low, B, decoded);
    
    EXPECT_EQ(decoded[0], x) << "X coordinate mismatch";
    EXPECT_EQ(decoded[1], y) << "Y coordinate mismatch";
    EXPECT_EQ(decoded[2], z) << "Z coordinate mismatch";
    EXPECT_EQ(decoded[3], m) << "M coordinate mismatch";
}

TEST_F(HilbertCurveTest, InterleaveBitsIsolation) {
    uint32_t original[4] = { 100, 200, 300, 400 };
    uint64_t high, low;
    InterleaveBits4D(original, B, &high, &low);
    
    uint32_t result[4];
    DeinterleaveBits4D(high, low, B, result);
    
    EXPECT_EQ(result[0], original[0]) << "X coordinate interleave/deinterleave failed";
    EXPECT_EQ(result[1], original[1]) << "Y coordinate interleave/deinterleave failed";
    EXPECT_EQ(result[2], original[2]) << "Z coordinate interleave/deinterleave failed";
    EXPECT_EQ(result[3], original[3]) << "M coordinate interleave/deinterleave failed";
}

TEST_F(HilbertCurveTest, FullRoundTripRandomValues) {
    std::mt19937 rng(42);
    std::uniform_int_distribution<uint32_t> dist(0, MAX_VAL);
    
    constexpr int TEST_COUNT = 1000;
    for (int i = 0; i < TEST_COUNT; ++i) {
        uint32_t x = dist(rng);
        uint32_t y = dist(rng);
        uint32_t z = dist(rng);
        uint32_t m = dist(rng);

        uint64_t high, low;
        HilbertEncode4D(x, y, z, m, B, &high, &low);

        uint32_t decoded[N];
        HilbertDecode4D(high, low, B, decoded);

        EXPECT_EQ(decoded[0], x) << "Round trip failed at iteration " << i << " for X";
        EXPECT_EQ(decoded[1], y) << "Round trip failed at iteration " << i << " for Y";
        EXPECT_EQ(decoded[2], z) << "Round trip failed at iteration " << i << " for Z";
        EXPECT_EQ(decoded[3], m) << "Round trip failed at iteration " << i << " for M";
    }
}

TEST_F(HilbertCurveTest, BoundaryValues) {
    // Test zero values
    uint32_t zeros[4] = {0, 0, 0, 0};
    uint64_t high, low;
    HilbertEncode4D(zeros[0], zeros[1], zeros[2], zeros[3], B, &high, &low);
    uint32_t decoded[N];
    HilbertDecode4D(high, low, B, decoded);
    EXPECT_EQ(decoded[0], 0u);
    EXPECT_EQ(decoded[1], 0u);
    EXPECT_EQ(decoded[2], 0u);
    EXPECT_EQ(decoded[3], 0u);
    
    // Test maximum values
    uint32_t maxvals[4] = {MAX_VAL, MAX_VAL, MAX_VAL, MAX_VAL};
    HilbertEncode4D(maxvals[0], maxvals[1], maxvals[2], maxvals[3], B, &high, &low);
    HilbertDecode4D(high, low, B, decoded);
    EXPECT_EQ(decoded[0], MAX_VAL);
    EXPECT_EQ(decoded[1], MAX_VAL);
    EXPECT_EQ(decoded[2], MAX_VAL);
    EXPECT_EQ(decoded[3], MAX_VAL);
}
