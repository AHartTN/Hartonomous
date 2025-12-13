/*
 * lmds_projector.cpp - LMDS projection implementation
 * 
 * Uses Eigen library for linear algebra operations.
 * Implements Landmark Multidimensional Scaling.
 */

#include <Eigen/Dense>
#include <vector>
#include <cmath>

extern "C" {

struct Point4D {
    double x, y, z, m;
};

struct LandmarkSet {
    int count;
    Point4D* landmarks;
    double** distances; // Distance matrix
};

// Calculate stress score for atom
double calculate_stress(
    Point4D current,
    Point4D* neighbors,
    double* true_distances,
    int neighbor_count
) {
    double stress = 0.0;
    
    for (int i = 0; i < neighbor_count; i++) {
        // Euclidean distance in current embedding
        double dx = current.x - neighbors[i].x;
        double dy = current.y - neighbors[i].y;
        double dz = current.z - neighbors[i].z;
        double dm = current.m - neighbors[i].m;
        
        double embedded_dist = std::sqrt(dx*dx + dy*dy + dz*dz + dm*dm);
        
        // Stress = squared difference
        double diff = embedded_dist - true_distances[i];
        stress += diff * diff;
    }
    
    return stress / neighbor_count;
}

// Project atom using LMDS
Point4D lmds_project(
    double* landmark_distances,
    LandmarkSet* landmarks,
    int num_landmarks
) {
    // Convert to Eigen matrix
    Eigen::VectorXd dists(num_landmarks);
    for (int i = 0; i < num_landmarks; i++) {
        dists(i) = landmark_distances[i];
    }
    
    // Build landmark position matrix
    Eigen::MatrixXd L(num_landmarks, 4);
    for (int i = 0; i < num_landmarks; i++) {
        L(i, 0) = landmarks->landmarks[i].x;
        L(i, 1) = landmarks->landmarks[i].y;
        L(i, 2) = landmarks->landmarks[i].z;
        L(i, 3) = landmarks->landmarks[i].m;
    }
    
    // Compute landmark distance matrix D
    Eigen::MatrixXd D(num_landmarks, num_landmarks);
    for (int i = 0; i < num_landmarks; i++) {
        for (int j = 0; j < num_landmarks; j++) {
            D(i, j) = landmarks->distances[i][j];
        }
    }
    
    // Double centering: B = -0.5 * H * D^2 * H
    Eigen::VectorXd ones = Eigen::VectorXd::Ones(num_landmarks);
    Eigen::MatrixXd H = Eigen::MatrixXd::Identity(num_landmarks, num_landmarks) 
                       - (ones * ones.transpose()) / num_landmarks;
    
    Eigen::MatrixXd D2 = D.array().square();
    Eigen::MatrixXd B = -0.5 * H * D2 * H;
    
    // Eigendecomposition
    Eigen::SelfAdjointEigenSolver<Eigen::MatrixXd> solver(B);
    Eigen::VectorXd eigenvalues = solver.eigenvalues();
    Eigen::MatrixXd eigenvectors = solver.eigenvectors();
    
    // Take top 4 eigenvalues/vectors
    Eigen::VectorXd lambda(4);
    Eigen::MatrixXd V(num_landmarks, 4);
    
    for (int i = 0; i < 4; i++) {
        int idx = num_landmarks - 1 - i;
        lambda(i) = std::max(0.0, eigenvalues(idx));
        V.col(i) = eigenvectors.col(idx);
    }
    
    // Coordinates: X = V * sqrt(Lambda)
    Eigen::MatrixXd X = V * lambda.cwiseSqrt().asDiagonal();
    
    // Project new point using pseudo-inverse
    Eigen::MatrixXd X_pinv = X.completeOrthogonalDecomposition().pseudoInverse();
    
    // New point distances
    Eigen::VectorXd d_new2 = dists.array().square();
    Eigen::VectorXd d_L2 = D.rowwise().squaredNorm() / num_landmarks;
    Eigen::VectorXd b_new = -0.5 * (d_new2 - d_L2);
    
    // Project
    Eigen::VectorXd coords = X_pinv.transpose() * b_new;
    
    Point4D result;
    result.x = coords(0);
    result.y = coords(1);
    result.z = coords(2);
    result.m = coords(3);
    
    return result;
}

// Modified Gram-Schmidt orthonormalization
void gram_schmidt_orthonormalize(
    Point4D* points,
    int count
) {
    Eigen::MatrixXd M(count, 4);
    
    // Load points
    for (int i = 0; i < count; i++) {
        M(i, 0) = points[i].x;
        M(i, 1) = points[i].y;
        M(i, 2) = points[i].z;
        M(i, 3) = points[i].m;
    }
    
    // Modified Gram-Schmidt
    for (int j = 0; j < 4; j++) {
        // Normalize j-th column
        double norm = M.col(j).norm();
        if (norm > 1e-10) {
            M.col(j) /= norm;
        }
        
        // Orthogonalize subsequent columns
        for (int k = j + 1; k < 4; k++) {
            double proj = M.col(j).dot(M.col(k));
            M.col(k) -= proj * M.col(j);
        }
    }
    
    // Write back
    for (int i = 0; i < count; i++) {
        points[i].x = M(i, 0);
        points[i].y = M(i, 1);
        points[i].z = M(i, 2);
        points[i].m = M(i, 3);
    }
}

} // extern "C"
