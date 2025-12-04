using Hartonomous.Core.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Interface for GPU-accelerated spatial and computational operations.
/// Provides high-performance alternatives to standard CPU-based operations.
/// </summary>
public interface IGpuService
{
    /// <summary>
    /// Find k-nearest neighbors to target coordinate using GPU acceleration.
    /// </summary>
    /// <param name="targetCoordinate">Target spatial coordinate</param>
    /// <param name="k">Number of nearest neighbors to find</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of constant IDs with distances, sorted by distance ascending</returns>
    Task<IReadOnlyList<(Guid ConstantId, double Distance)>> FindNearestNeighborsAsync(
        SpatialCoordinate targetCoordinate,
        int k,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform DBSCAN clustering on active constants to detect spatial landmarks.
    /// </summary>
    /// <param name="epsilon">Maximum distance for neighborhood (typically 0.05 to 0.2)</param>
    /// <param name="minSamples">Minimum points to form cluster (typically 5-20)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping constant IDs to cluster IDs (-1 for noise)</returns>
    Task<IReadOnlyDictionary<Guid, int>> PerformSpatialClusteringAsync(
        double epsilon,
        int minSamples,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find similar constants using cosine similarity of spatial embeddings.
    /// </summary>
    /// <param name="hash">Hash of target constant</param>
    /// <param name="topK">Number of similar constants to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of constant IDs with similarity scores (0-1), sorted descending</returns>
    Task<IReadOnlyList<(Guid ConstantId, double Similarity)>> FindSimilarConstantsAsync(
        Hash256 hash,
        int topK,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Learn BPE vocabulary from recent content ingestions using GPU acceleration.
    /// </summary>
    /// <param name="maxVocabSize">Maximum vocabulary size to learn</param>
    /// <param name="minFrequency">Minimum frequency for pairs to be merged</param>
    /// <param name="sampleSize">Number of recent ingestions to sample</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of learned byte pairs with frequencies</returns>
    Task<IReadOnlyList<(byte[] BytePair, int Frequency)>> LearnBpeVocabularyAsync(
        int maxVocabSize,
        int minFrequency,
        int sampleSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compute Hilbert curve indices for batch of constants using GPU acceleration.
    /// </summary>
    /// <param name="bitsPerDimension">Bits per dimension (default 21 for 63-bit total)</param>
    /// <param name="batchSize">Maximum number of constants to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping constant IDs to Hilbert indices</returns>
    Task<IReadOnlyDictionary<Guid, ulong>> ComputeHilbertIndicesBatchAsync(
        int bitsPerDimension,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check GPU availability and capabilities.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>GPU capability information</returns>
    Task<GpuCapabilities> CheckGpuAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect spatial landmarks from clustering results.
    /// </summary>
    /// <param name="epsilon">DBSCAN epsilon parameter</param>
    /// <param name="minSamples">DBSCAN minimum samples parameter</param>
    /// <param name="minClusterSize">Minimum cluster size to be considered landmark</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of detected landmark candidates with centroids and member counts</returns>
    Task<IReadOnlyList<LandmarkCandidate>> DetectLandmarksAsync(
        double epsilon,
        int minSamples,
        int minClusterSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// GPU capability information.
/// </summary>
public sealed record GpuCapabilities
{
    public bool HasCuPy { get; init; }
    public bool HasCuMl { get; init; }
    public int GpuCount { get; init; }
    public long GpuMemoryMb { get; init; }
    public string? ErrorMessage { get; init; }
    
    public bool IsAvailable => HasCuPy && GpuCount > 0;
}

/// <summary>
/// Landmark candidate detected from spatial clustering.
/// </summary>
public sealed record LandmarkCandidate
{
    public required int ClusterId { get; init; }
    public required SpatialCoordinate Centroid { get; init; }
    public required int MemberCount { get; init; }
    public required string SuggestedName { get; init; }
}
