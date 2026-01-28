#pragma once

#include <Eigen/Core>
#include <Eigen/Sparse>
#include <Spectra/SymEigsSolver.h>
#include <Spectra/MatOp/SparseSymMatProd.h>
#include <vector>
#include <cstdint>

namespace hartonomous::ml {

/**
 * @brief Embedding Projection: N-dimensional → 4D via Laplacian Eigenmaps
 *
 * This class projects high-dimensional embeddings (from any AI model) to the
 * universal 4D space (S³) used by Hartonomous.
 *
 * Pipeline:
 *   1. Extract N-dimensional embeddings from AI model (e.g., 768D BERT, 4096D GPT)
 *   2. Build k-NN graph (semantic similarity connections)
 *   3. Compute graph Laplacian (L = D - W)
 *   4. Solve eigenvalue problem (find smallest eigenvectors)
 *   5. Extract first 4 eigenvectors → 4D coordinates
 *   6. Gram-Schmidt orthonormalization (ensure orthogonal basis)
 *   7. Project to S³ (normalize to unit sphere)
 *
 * Why Laplacian Eigenmaps?
 *   - Preserves local neighborhood structure
 *   - Non-linear dimensionality reduction
 *   - Spectral graph theory foundation
 *   - Optimal for manifold learning
 *
 * Why 4D?
 *   - S³ has sufficient expressiveness for complex semantics
 *   - Hopf fibration enables 3D visualization
 *   - Quaternion algebra for efficient operations
 *   - Hilbert curves for spatial indexing
 *
 * Result: ALL AI models → same 4D substrate (universal comparison)
 */
class EmbeddingProjection {
public:
    using VectorXd = Eigen::VectorXd;
    using MatrixXd = Eigen::MatrixXd;
    using SparseMatrix = Eigen::SparseMatrix<double>;
    using Vec4 = Eigen::Vector4d;

    /**
     * @brief Configuration for projection
     */
    struct Config {
        int k_neighbors = 10;              ///< k for k-NN graph construction
        double sigma = 1.0;                ///< Gaussian kernel width
        int num_eigenvectors = 4;          ///< Target dimensionality
        bool use_normalized_laplacian = true;  ///< Use normalized Laplacian (recommended)
        double eigenvalue_tolerance = 1e-6;    ///< Convergence tolerance for eigen solver
        int max_iterations = 1000;             ///< Max iterations for eigen solver
    };

    /**
     * @brief Initialize projector with configuration
     */
    EmbeddingProjection(const Config& config = Config())
        : config_(config) {}

    /**
     * @brief Project N-dimensional embeddings to 4D S³
     *
     * @param embeddings N×M matrix (N samples, M dimensions)
     * @return MatrixXd N×4 matrix (N samples in 4D)
     *
     * @throws std::runtime_error if projection fails
     */
    MatrixXd project_to_4d(const MatrixXd& embeddings) {
        const int n_samples = embeddings.rows();
        const int n_dims = embeddings.cols();

        if (n_samples < config_.num_eigenvectors) {
            throw std::runtime_error("Not enough samples for 4D projection");
        }

        // Step 1: Build k-NN graph
        SparseMatrix adjacency = build_knn_graph(embeddings);

        // Step 2: Compute graph Laplacian
        SparseMatrix laplacian = compute_laplacian(adjacency);

        // Step 3: Solve eigenvalue problem (find smallest eigenvectors)
        MatrixXd eigenvectors = solve_eigenvalue_problem(laplacian);

        // Step 4: Gram-Schmidt orthonormalization
        MatrixXd orthonormal = gram_schmidt(eigenvectors);

        // Step 5: Project to S³ (normalize each row to unit sphere)
        return project_to_s3(orthonormal);
    }

    /**
     * @brief Extract embeddings from AI model and project to 4D
     *
     * This is a convenience method that handles the full pipeline:
     *   - Load model embeddings (from checkpoint file)
     *   - Filter semantic edges (remove near-zero connections)
     *   - Project to 4D
     *
     * @param model_embeddings N×M matrix from AI model
     * @param sparsity_threshold Remove edges below this threshold
     * @return MatrixXd N×4 projected embeddings on S³
     */
    MatrixXd project_model_embeddings(
        const MatrixXd& model_embeddings,
        double sparsity_threshold = 0.01
    ) {
        // Apply sparsity filter (remove near-zero embeddings)
        MatrixXd sparse_embeddings = apply_sparsity(model_embeddings, sparsity_threshold);

        // Project to 4D
        return project_to_4d(sparse_embeddings);
    }

private:
    Config config_;

    /**
     * @brief Build k-NN graph from embeddings
     *
     * Constructs a sparse adjacency matrix where each node is connected
     * to its k nearest neighbors with Gaussian kernel weights.
     *
     * @param embeddings N×M matrix
     * @return SparseMatrix N×N adjacency matrix (symmetric)
     */
    SparseMatrix build_knn_graph(const MatrixXd& embeddings) {
        const int n = embeddings.rows();

        // Use Eigen's efficient sparse matrix builder
        std::vector<Eigen::Triplet<double>> triplets;
        triplets.reserve(n * config_.k_neighbors * 2); // *2 for symmetry

        #pragma omp parallel for
        for (int i = 0; i < n; ++i) {
            // Compute distances to all other points
            std::vector<std::pair<double, int>> distances;
            distances.reserve(n);

            for (int j = 0; j < n; ++j) {
                if (i == j) continue;

                double dist = (embeddings.row(i) - embeddings.row(j)).squaredNorm();
                distances.push_back({dist, j});
            }

            // Sort and take k nearest neighbors
            std::partial_sort(distances.begin(),
                            distances.begin() + config_.k_neighbors,
                            distances.end());

            // Add edges with Gaussian kernel weights
            #pragma omp critical
            {
                for (int k = 0; k < config_.k_neighbors; ++k) {
                    double dist_sq = distances[k].first;
                    int j = distances[k].second;

                    // Gaussian kernel: w = exp(-dist² / (2σ²))
                    double weight = std::exp(-dist_sq / (2.0 * config_.sigma * config_.sigma));

                    // Add symmetric edges
                    triplets.push_back({i, j, weight});
                    triplets.push_back({j, i, weight});
                }
            }
        }

        // Build sparse matrix
        SparseMatrix adjacency(n, n);
        adjacency.setFromTriplets(triplets.begin(), triplets.end());

        return adjacency;
    }

    /**
     * @brief Compute graph Laplacian
     *
     * L = D - W (unnormalized)
     * or
     * L = I - D^(-1/2) W D^(-1/2) (normalized, recommended)
     *
     * @param adjacency N×N adjacency matrix W
     * @return SparseMatrix N×N Laplacian matrix
     */
    SparseMatrix compute_laplacian(const SparseMatrix& adjacency) {
        const int n = adjacency.rows();

        // Compute degree matrix D
        VectorXd degree = VectorXd::Zero(n);
        for (int k = 0; k < adjacency.outerSize(); ++k) {
            for (SparseMatrix::InnerIterator it(adjacency, k); it; ++it) {
                degree[it.row()] += it.value();
            }
        }

        if (config_.use_normalized_laplacian) {
            // Normalized Laplacian: L = I - D^(-1/2) W D^(-1/2)

            // Compute D^(-1/2)
            VectorXd d_inv_sqrt = degree.array().sqrt().inverse();

            // Build L = I - D^(-1/2) W D^(-1/2)
            std::vector<Eigen::Triplet<double>> triplets;
            triplets.reserve(adjacency.nonZeros() + n);

            // Add identity diagonal
            for (int i = 0; i < n; ++i) {
                triplets.push_back({i, i, 1.0});
            }

            // Subtract normalized adjacency
            for (int k = 0; k < adjacency.outerSize(); ++k) {
                for (SparseMatrix::InnerIterator it(adjacency, k); it; ++it) {
                    double normalized_weight = -d_inv_sqrt[it.row()] * it.value() * d_inv_sqrt[it.col()];
                    triplets.push_back({it.row(), it.col(), normalized_weight});
                }
            }

            SparseMatrix laplacian(n, n);
            laplacian.setFromTriplets(triplets.begin(), triplets.end());
            return laplacian;

        } else {
            // Unnormalized Laplacian: L = D - W

            std::vector<Eigen::Triplet<double>> triplets;
            triplets.reserve(adjacency.nonZeros() + n);

            // Add degree diagonal
            for (int i = 0; i < n; ++i) {
                triplets.push_back({i, i, degree[i]});
            }

            // Subtract adjacency
            for (int k = 0; k < adjacency.outerSize(); ++k) {
                for (SparseMatrix::InnerIterator it(adjacency, k); it; ++it) {
                    triplets.push_back({it.row(), it.col(), -it.value()});
                }
            }

            SparseMatrix laplacian(n, n);
            laplacian.setFromTriplets(triplets.begin(), triplets.end());
            return laplacian;
        }
    }

    /**
     * @brief Solve eigenvalue problem using Spectra
     *
     * Finds the smallest eigenvectors of the Laplacian (skip first trivial eigenvector).
     *
     * @param laplacian N×N Laplacian matrix
     * @return MatrixXd N×4 matrix (first 4 non-trivial eigenvectors)
     */
    MatrixXd solve_eigenvalue_problem(const SparseMatrix& laplacian) {
        const int n = laplacian.rows();

        // Use Spectra for efficient sparse eigenvalue computation
        Spectra::SparseSymMatProd<double> op(laplacian);

        // We want the smallest eigenvalues (after the trivial zero eigenvalue)
        // So we request (num_eigenvectors + 1) and skip the first
        int num_compute = config_.num_eigenvectors + 1;

        Spectra::SymEigsSolver<Spectra::SparseSymMatProd<double>> eigs(
            op,
            num_compute,
            2 * num_compute  // Size of Krylov subspace
        );

        // Initialize and compute
        eigs.init();
        int nconv = eigs.compute(
            Spectra::SortRule::SmallestAlge,
            config_.max_iterations,
            config_.eigenvalue_tolerance
        );

        if (eigs.info() != Spectra::CompInfo::Successful) {
            throw std::runtime_error("Eigenvalue computation failed");
        }

        // Extract eigenvectors (skip first trivial eigenvector)
        MatrixXd eigenvectors = eigs.eigenvectors().rightCols(config_.num_eigenvectors);

        return eigenvectors;
    }

    /**
     * @brief Gram-Schmidt orthonormalization
     *
     * Ensures the basis vectors are orthogonal and normalized.
     *
     * @param vectors N×4 matrix
     * @return MatrixXd N×4 orthonormal matrix
     */
    MatrixXd gram_schmidt(const MatrixXd& vectors) {
        MatrixXd result = vectors;
        const int n_cols = result.cols();

        for (int i = 0; i < n_cols; ++i) {
            // Orthogonalize against previous vectors
            for (int j = 0; j < i; ++j) {
                double proj = result.col(i).dot(result.col(j));
                result.col(i) -= proj * result.col(j);
            }

            // Normalize
            double norm = result.col(i).norm();
            if (norm > 1e-10) {
                result.col(i) /= norm;
            } else {
                // Degenerate case: replace with random orthogonal vector
                result.col(i).setRandom();
                result.col(i).normalize();
            }
        }

        return result;
    }

    /**
     * @brief Project to S³ (unit sphere in 4D)
     *
     * Normalizes each row to lie on the 3-sphere.
     *
     * @param coords N×4 matrix
     * @return MatrixXd N×4 matrix on S³
     */
    MatrixXd project_to_s3(const MatrixXd& coords) {
        MatrixXd result = coords;
        const int n_rows = result.rows();

        #pragma omp parallel for
        for (int i = 0; i < n_rows; ++i) {
            double norm = result.row(i).norm();
            if (norm > 1e-10) {
                result.row(i) /= norm;
            } else {
                // Degenerate case: place at default point
                result.row(i) << 1.0, 0.0, 0.0, 0.0;
            }
        }

        return result;
    }

    /**
     * @brief Apply sparsity filter (remove near-zero values)
     *
     * @param embeddings N×M matrix
     * @param threshold Remove values below this absolute threshold
     * @return MatrixXd Sparse filtered matrix
     */
    MatrixXd apply_sparsity(const MatrixXd& embeddings, double threshold) {
        MatrixXd result = embeddings;

        #pragma omp parallel for collapse(2)
        for (int i = 0; i < result.rows(); ++i) {
            for (int j = 0; j < result.cols(); ++j) {
                if (std::abs(result(i, j)) < threshold) {
                    result(i, j) = 0.0;
                }
            }
        }

        return result;
    }
};

/**
 * @brief Batch projection for large models
 *
 * Projects embeddings in batches to avoid memory issues.
 */
class BatchEmbeddingProjection {
public:
    /**
     * @brief Project large embedding matrix in batches
     *
     * @param embeddings N×M matrix (can be very large)
     * @param batch_size Process this many samples at once
     * @return MatrixXd N×4 projected embeddings
     */
    static MatrixXd project_batched(
        const MatrixXd& embeddings,
        int batch_size = 10000,
        const EmbeddingProjection::Config& config = EmbeddingProjection::Config()
    ) {
        const int n_samples = embeddings.rows();
        const int n_batches = (n_samples + batch_size - 1) / batch_size;

        MatrixXd result(n_samples, 4);

        #pragma omp parallel for
        for (int batch_idx = 0; batch_idx < n_batches; ++batch_idx) {
            int start = batch_idx * batch_size;
            int end = std::min(start + batch_size, n_samples);
            int batch_size_actual = end - start;

            // Extract batch
            MatrixXd batch = embeddings.middleRows(start, batch_size_actual);

            // Project batch
            EmbeddingProjection projector(config);
            MatrixXd projected = projector.project_to_4d(batch);

            // Store results
            result.middleRows(start, batch_size_actual) = projected;
        }

        return result;
    }
};

} // namespace hartonomous::ml
