#include "skilling.h"
#include <vector>
#include <cstdint>

// Helper to interleave bits (Encode)
// Packs N coordinates (B bits each) into 2 ulongs (High/Low) for 4D (84 bits total)
void InterleaveBits4D(uint32_t* X, int b, uint64_t* high, uint64_t* low) {
    *high = 0;
    *low = 0;
    int n = 4;

    for (int i = b - 1; i >= 0; i--) {
        for (int j = 0; j < n; j++) {
            uint64_t bit = (X[j] >> i) & 1;
            int pos = i * n + j; // 0 is LSB
            
            if (pos >= 64) {
                // Should not happen for 84 bits (max pos 83), but if we use >21 bits...
                // Wait, splitting into 2 ulongs:
                // Low: bits 0-63? Or split evenly?
                // C# implementation splits at 42.
                // Let's match C# split: Low has bits 0-41, High has bits 42-83.
                
                // Actually, standard ulong is 64 bits.
                // If we want to return 2 ulongs, let's fill them fully?
                // The C# code used a 42/42 split. Let's stick to that for consistency or
                // standardize on 64/remaining. 
                // 42/42 is cleaner for symmetry but inefficient.
                // Let's map to the exact same split as C# for compatibility.
                
                // C# Logic:
                // if (bitPos >= 42) { ... high ... } else { ... low ... }
                
                if (pos >= 42) {
                    *high |= (bit << (pos - 42));
                } else {
                    *low |= (bit << pos);
                }
            } else {
                 // Standard 64-bit logic would go here if we changed it.
                 // But respecting the split:
                if (pos >= 42) {
                    *high |= (bit << (pos - 42));
                } else {
                    *low |= (bit << pos);
                }
            }
        }
    }
}

// Helper to de-interleave bits (Decode)
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

extern "C" {

    HARTONOMOUS_API void HilbertEncode4D(
        uint32_t x, uint32_t y, uint32_t z, uint32_t m,
        int precision,
        uint64_t* resultHigh, uint64_t* resultLow)
    {
        uint32_t X[4] = { x, y, z, m };
        
        // 1. Coordinate to Transpose (Inverse Skilling)
        // Skilling's AxestoTranspose converts Coordinates -> Transposed (Hilbert) index bits
        // Wait, "AxestoTranspose" means "Axes (Coords) -> Transpose (Index)".
        // So Encode should call AxesToTranspose.
        AxesToTranspose(X, 4, precision);
        
        // 2. Interleave bits to form the integer index
        InterleaveBits4D(X, precision, resultHigh, resultLow);
    }

    HARTONOMOUS_API void HilbertDecode4D(
        uint64_t indexHigh, uint64_t indexLow,
        int precision,
        uint32_t* resultCoords)
    {
        uint32_t X[4];
        
        // 1. De-interleave integer index -> Transposed bits
        DeinterleaveBits4D(indexHigh, indexLow, precision, X);
        
        // 2. Transpose to Axes (Skilling)
        // Converts Transposed (Hilbert) index bits -> Coordinates
        TransposeToAxes(X, 4, precision);
        
        resultCoords[0] = X[0];
        resultCoords[1] = X[1];
        resultCoords[2] = X[2];
        resultCoords[3] = X[3];
    }

}
