#ifndef HARTONOMOUS_SKILLING_H
#define HARTONOMOUS_SKILLING_H

#include <cstdint>

#ifdef __cplusplus
extern "C" {
#endif

// Define export macro for Windows/Linux
#if defined(_WIN32)
    #define HARTONOMOUS_API __declspec(dllexport)
#else
    #define HARTONOMOUS_API
#endif

// Skilling's functions adapted for arbitrary N dimensions and B bits
HARTONOMOUS_API void TransposeToAxes(uint32_t* X, int n, int b);
HARTONOMOUS_API void AxesToTranspose(uint32_t* X, int n, int b);

#ifdef __cplusplus
}
#endif

#endif // HARTONOMOUS_SKILLING_H
