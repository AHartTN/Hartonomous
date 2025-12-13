/*
 * lmds_projector.h - LMDS projection C-compatible interface
 */

#ifndef LMDS_PROJECTOR_H
#define LMDS_PROJECTOR_H

/* C-compatible struct definitions */
typedef struct Point4D {
    double x;
    double y;
    double z;
    double m;
} Point4D;

typedef struct LandmarkSet {
    int count;
    Point4D* landmarks;
    double** distances;
} LandmarkSet;

#ifdef __cplusplus
extern "C" {
#endif

/* Calculate stress for an atom */
double calculate_stress(
    Point4D current,
    Point4D* neighbors,
    double* true_distances,
    int neighbor_count
);

/* Project atom using LMDS - output via pointer */
void lmds_project(
    double* landmark_distances,
    LandmarkSet* landmarks,
    int num_landmarks,
    Point4D* out_position
);

/* Gram-Schmidt orthonormalization */
void gram_schmidt_orthonormalize(
    Point4D* points,
    int count
);

#ifdef __cplusplus
}
#endif

#endif /* LMDS_PROJECTOR_H */
