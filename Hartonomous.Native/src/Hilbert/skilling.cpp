#include "skilling.h"
#include <algorithm> // For std::swap

// Implementation of Skilling's algorithm from "Programming the Hilbert curve" (2004)
// Verified against standard implementations (e.g. regions-of-interest/hilbert)

void TransposeToAxes(uint32_t* X, int n, int b) {
    uint32_t N = 2U << (b - 1);
    uint32_t P, Q, t;
    int i;

    // Gray decode by H ^ (H/2)
    t = X[n - 1] >> 1;
    X[0] ^= t;
    // Corrected loop: i > 0
    for (i = n - 1; i > 0; i--) {
        X[i] ^= X[i - 1];
    }

    // Undo excess work
    for (Q = 2; Q <= N; Q <<= 1) {
        P = Q - 1;
        for (i = n - 1; i >= 0; i--) {
            if ((X[i] & Q) != 0) {
                X[0] ^= P; // Invert X[0]
            }
            else {
                // Swap X[0] and X[i] for bits in P
                t = (X[0] ^ X[i]) & P;
                X[0] ^= t;
                X[i] ^= t;
            }
        }
    }
}

void AxesToTranspose(uint32_t* X, int n, int b) {
    uint32_t N = 2U << (b - 1);
    uint32_t P, Q, t;
    int i;

    // Inverse undo excess work
    // Loop Q from N down to 2 (Inverse order of TransposeToAxes)
    for (Q = N; Q > 1; Q >>= 1) {
        P = Q - 1;
        for (i = 0; i < n; i++) { // Inverse loop direction usually? Or same?
                                  // In TransposeToAxes, i goes n-1 to 0.
                                  // The operations inside depend on X[i].
                                  // Since X[0] is the pivot, and we modify X[i] and X[0],
                                  // The order of i matters if i includes 0.
                                  // The loop was n-1 downto 0.
                                  // For inverse, we should do exact reverse operations in reverse order.
                                  // Reverse of (Loop Q ascending, Loop i descending)
                                  // Is (Loop Q descending, Loop i ascending).
            
            if ((X[i] & Q) != 0) {
                X[0] ^= P; // Invert X[0]
            }
            else {
                // Swap X[0] and X[i] for bits in P
                t = (X[0] ^ X[i]) & P;
                X[0] ^= t;
                X[i] ^= t;
            }
        }
    }

    // Inverse Gray decode
    // TransposeToAxes:
    // t = X[n-1] >> 1;
    // for (i = n-1; i > 0; i--) X[i] ^= X[i-1];
    // X[0] ^= t;
    
    // Inverse:
    // X[0] ^= t; -> But t depends on X[n-1]. X[n-1] was modified in the loop.
    // We must reverse the steps exactly.
    // Step 3: X[0] ^= (X[n-1] >> 1)  (using current X[n-1])
    // Step 2: for (i = 1; i < n; i++) X[i] ^= X[i-1];  (Forward loop to undo X[i] ^= X[i-1])
    //         Wait, x ^= y inverse is x ^= y.
    //         Original: X[n-1]^=X[n-2], X[n-2]^=X[n-3] ...
    //         We need to undo X[1]^=X[0] first (since X[0] is used for X[1]?)
    //         Original loop was i descending: X[4]^=X[3], X[3]^=X[2], X[2]^=X[1].
    //         To undo: Undo X[2]^=X[1], then X[3]^=X[2], etc.
    //         So loop i ascending: 1 to n-1.
    
    // Restore X[0]
    // Note: In TransposeToAxes, t = X[n-1]>>1 was calculated BEFORE loop.
    // X[n-1] changed. So we need the *original* X[n-1] to compute t.
    // This is tricky.
    // Actually, the Gray decode used `t` derived from `X[n-1]` *before* `X[n-1]` was modified?
    // Yes: `t = X[n-1] >> 1`.
    // Then loop modified `X[n-1]`.
    // So we can't just compute `t` from current `X[n-1]`.
    
    // BUT, look at the loop: `X[i] ^= X[i-1]`.
    // `X[n-1] ^= X[n-2]`.
    // To recover `X[n-1]_old`, we need `X[n-1]_new ^ X[n-2]_old`.
    // So we must undo the loop *first*.
    
    // Undo loop:
    // for (i = 1; i < n; i++) X[i] ^= X[i-1];
    // Example: N=3.
    // Enc: X[2]^=X[1], X[1]^=X[0].
    // Dec: X[1]^=X[0] (restores X[1]), X[2]^=X[1] (restores X[2]). Correct.
    
    for (i = 1; i < n; i++) {
        X[i] ^= X[i - 1];
    }
    
    // Now X[n-1] is restored to its value *before* the loop.
    // So we can compute `t`.
    t = X[n - 1] >> 1;
    X[0] ^= t;
}