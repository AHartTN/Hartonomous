---
name: ml-spectral-projection
description: Project high-dimensional AI model embeddings to 4D S³ via Laplacian Eigenmaps. Use when modifying 'Engine/src/ml/' or adding support for new embedding sources.
---

# ML Spectral Projection

This skill manages the dimensionality reduction pipeline from $N$-dimensional vector embeddings to the universal 4D geometric substrate ($S^3$). 

## The Laplacian Eigenmaps Pipeline

Implementation: `Engine/include/ml/embedding_projection.hpp`

1.  **k-NN Graph Construction**:
    *   Utilize **HNSWLib** (`hnswlib::HierarchicalNSW`) for $O(n \log n)$ approximate nearest neighbor search.
    *   Calculate semantic weights using a **Gaussian Kernel**: $W_{ij} = \exp(-\|x_i - x_j\|^2 / 2\sigma^2)$.
2.  **Graph Laplacian Computation**:
    *   Compute Degree matrix $D_{ii} = \sum_j W_{ij}$.
    *   **Normalized Laplacian**: $L = I - D^{-1/2} W D^{-1/2}$. This preserves neighborhood structure more robustly.
3.  **Eigenvalue Solving**:
    *   Utilize **Spectra** (`Spectra::SymEigsSolver`) to find the smallest non-trivial eigenvectors of $L$.
    *   Target: First 4 non-trivial eigenvectors (skipping the zero eigenvalue).
4.  **Gram-Schmidt Orthonormalization**:
    *   Implemented via **MKL-accelerated Householder QR decomposition**.
    *   Ensures the resulting 4D basis is perfectly orthogonal.
5.  **S³ Projection**:
    *   Normalize resulting 4D vectors to unit length: $v' = v / \|v\|$.

## Mathematical Libraries
- **Intel MKL**: Backend for all dense BLAS/LAPACK operations (DSYEV, QR).
- **Eigen**: High-level matrix interface and sparse matrix support.
- **Spectra**: Sparse symmetric eigenvalue solver based on Lanczos iteration.
- **HNSWLib**: Fast ANN search with AUTO-SIMD (AVX-512) optimization.

## Verification
Ensure the resulting coordinates lie on the surface of the 4D unit ball ($\text{norm} \approx 1.0$) and preserve local semantic similarity.