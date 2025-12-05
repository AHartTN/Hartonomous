#include <iostream>
#include <vector>
#include <cassert>
#include <random>
#include "skilling.h"

// Prototypes for the functions in hartonomous_native.cpp
// We need to expose them or copy them to test them. 
// Since they are not in a header, I'll copy them here for testing purposes.
// This confirms logic correctness.

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

int main() {
    std::cout << "Running Native C++ Tests (Full Round Trip + Interleave)..." << std::endl;

    const int B = 21;
    const int N = 4;
    const uint32_t MAX_VAL = (1 << B) - 1;

    std::mt19937 rng(42);
    std::uniform_int_distribution<uint32_t> dist(0, MAX_VAL);

    // Diagnostic: Print specific case for C# comparison
    {
        uint32_t x=100, y=200, z=300, m=400;
        uint64_t h, l;
        HilbertEncode4D(x, y, z, m, B, &h, &l);
        std::cout << "DIAGNOSTIC (100,200,300,400): High=" << h << ", Low=" << l << std::endl;
        
        uint32_t decodedCoords[N];
        HilbertDecode4D(h, l, B, decodedCoords);
        std::cout << "DIAGNOSTIC DECODE: " << decodedCoords[0] << "," << decodedCoords[1] << "," << decodedCoords[2] << "," << decodedCoords[3] << std::endl;
    }

    // Test Interleave/Deinterleave Isolation
    {
        std::cout << "Testing Interleave/Deinterleave Isolation..." << std::endl;
        uint32_t X[4] = { 100, 200, 300, 400 };
        uint64_t h, l;
        InterleaveBits4D(X, B, &h, &l);
        uint32_t Y[4];
        DeinterleaveBits4D(h, l, B, Y);
        
        if (X[0] == Y[0] && X[1] == Y[1] && X[2] == Y[2] && X[3] == Y[3]) {
            std::cout << "Interleave Isolation Passed." << std::endl;
        } else {
            std::cout << "Interleave Isolation FAILED." << std::endl;
            std::cout << "Orig: " << X[0] << "," << X[1] << "," << X[2] << "," << X[3] << std::endl;
            std::cout << "Res:  " << Y[0] << "," << Y[1] << "," << Y[2] << "," << Y[3] << std::endl;
            return 1;
        }
    }

    // Full Round Trip
    int passed = 0;
    int total = 1000;

    for (int i = 0; i < total; ++i) {
        uint32_t x = dist(rng);
        uint32_t y = dist(rng);
        uint32_t z = dist(rng);
        uint32_t m = dist(rng);

        uint64_t high, low;
        HilbertEncode4D(x, y, z, m, B, &high, &low);

        uint32_t decodedCoords[N];
        HilbertDecode4D(high, low, B, decodedCoords);

        if (x == decodedCoords[0] && y == decodedCoords[1] && z == decodedCoords[2] && m == decodedCoords[3]) {
            passed++;
        } else {
            std::cout << "Mismatch at iteration " << i << std::endl;
            std::cout << "Original: " << x << ", " << y << ", " << z << ", " << m << std::endl;
            std::cout << "Decoded:  " << decodedCoords[0] << ", " << decodedCoords[1] << ", " << decodedCoords[2] << ", " << decodedCoords[3] << std::endl;
            return 1; // Fail fast
        }
    }

    std::cout << "Tests Passed: " << passed << "/" << total << std::endl;
    return 0;
}
