/*
 * lmds_projector.h - LMDS projection header
 */

#ifndef LMDS_PROJECTOR_H
#define LMDS_PROJECTOR_H

#ifdef __cplusplus
extern "C" {
#endif

struct Point4D {
    double x, y, z, m;
};

struct LandmarkSet {
    int count;
    Point4D* landmarks;
    double** distances;
};

// Calculate stress score
double calculate_stress(
    Point4D current,
    Point4D* neighbors,
    double* true_distances,
    int neighbor_count
);

// LMDS projection
Point4D lmds_project(
    double* landmark_distances,
    LandmarkSet* landmarks,
    int num_landmarks
);

// Gram-Schmidt orthonormalization
void gram_schmidt_orthonormalize(
    Point4D* points,
    int count
);

#ifdef __cplusplus
}
#endif

#endif // LMDS_PROJECTOR_H
