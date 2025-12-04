using Dapper;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hartonomous.Infrastructure.Services;

/// <summary>
/// GPU-accelerated service implementation using PL/Python functions.
/// Provides high-performance spatial operations and machine learning capabilities.
/// </summary>
public sealed class GpuService : IGpuService
{
    private readonly string _connectionString;
    private readonly ILogger<GpuService> _logger;
    private GpuCapabilities? _cachedCapabilities;

    public GpuService(string connectionString, ILogger<GpuService> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<(Guid ConstantId, double Distance)>> FindNearestNeighborsAsync(
        SpatialCoordinate targetCoordinate,
        int k,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetCoordinate);
        if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), "k must be positive");

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = @"
                SELECT constant_id, distance 
                FROM gpu_spatial_knn(@targetX, @targetY, @targetZ, @k)
                ORDER BY distance";

            var results = await connection.QueryAsync<KnnResult>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        targetX = targetCoordinate.X,
                        targetY = targetCoordinate.Y,
                        targetZ = targetCoordinate.Z,
                        k
                    },
                    cancellationToken: cancellationToken));

            var list = results.Select(r => (r.ConstantId, r.Distance)).ToList();
            
            _logger.LogInformation(
                "GPU k-NN search found {Count} neighbors for target ({X:F3}, {Y:F3}, {Z:F3})",
                list.Count, targetCoordinate.X, targetCoordinate.Y, targetCoordinate.Z);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing GPU k-NN search");
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> PerformSpatialClusteringAsync(
        double epsilon,
        int minSamples,
        CancellationToken cancellationToken = default)
    {
        if (epsilon <= 0) throw new ArgumentOutOfRangeException(nameof(epsilon), "epsilon must be positive");
        if (minSamples <= 0) throw new ArgumentOutOfRangeException(nameof(minSamples), "minSamples must be positive");

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = @"
                SELECT constant_id, cluster_id 
                FROM gpu_spatial_clustering(@epsilon, @minSamples)";

            var results = await connection.QueryAsync<ClusterResult>(
                new CommandDefinition(
                    sql,
                    new { epsilon, minSamples },
                    cancellationToken: cancellationToken));

            var dictionary = results.ToDictionary(r => r.ConstantId, r => r.ClusterId);
            
            var clusterCount = dictionary.Values.Where(c => c >= 0).Distinct().Count();
            var noiseCount = dictionary.Values.Count(c => c < 0);
            
            _logger.LogInformation(
                "GPU clustering found {ClusterCount} clusters with {NoiseCount} noise points (eps={Epsilon:F3}, minSamples={MinSamples})",
                clusterCount, noiseCount, epsilon, minSamples);

            return dictionary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing GPU clustering");
            throw;
        }
    }

    public async Task<IReadOnlyList<(Guid ConstantId, double Similarity)>> FindSimilarConstantsAsync(
        Hash256 hash,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive");

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = @"
                SELECT constant_id, similarity 
                FROM gpu_similarity_search(@hash, @topK)
                ORDER BY similarity DESC";

            var results = await connection.QueryAsync<SimilarityResult>(
                new CommandDefinition(
                    sql,
                    new { hash = hash.Hex, topK },
                    cancellationToken: cancellationToken));

            var list = results.Select(r => (r.ConstantId, r.Similarity)).ToList();
            
            _logger.LogInformation(
                "GPU similarity search found {Count} similar constants for hash {Hash}",
                list.Count, hash.Hex[..16] + "...");

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing GPU similarity search");
            throw;
        }
    }

    public async Task<IReadOnlyList<(byte[] BytePair, int Frequency)>> LearnBpeVocabularyAsync(
        int maxVocabSize,
        int minFrequency,
        int sampleSize,
        CancellationToken cancellationToken = default)
    {
        if (maxVocabSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxVocabSize));
        if (minFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(minFrequency));
        if (sampleSize <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSize));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = @"
                SELECT byte_pair, frequency 
                FROM gpu_bpe_learn(@maxVocabSize, @minFrequency, @sampleSize)
                ORDER BY frequency DESC";

            var results = await connection.QueryAsync<BpeResult>(
                new CommandDefinition(
                    sql,
                    new { maxVocabSize, minFrequency, sampleSize },
                    cancellationToken: cancellationToken));

            var list = results.Select(r => (r.BytePair, r.Frequency)).ToList();
            
            _logger.LogInformation(
                "GPU BPE learning discovered {Count} merges from {SampleSize} samples (maxVocab={MaxVocab}, minFreq={MinFreq})",
                list.Count, sampleSize, maxVocabSize, minFrequency);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing GPU BPE learning");
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, ulong>> ComputeHilbertIndicesBatchAsync(
        int bitsPerDimension,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (bitsPerDimension <= 0 || bitsPerDimension > 21) 
            throw new ArgumentOutOfRangeException(nameof(bitsPerDimension), "Must be between 1 and 21");
        if (batchSize <= 0) 
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = @"
                SELECT constant_id, hilbert_index 
                FROM gpu_hilbert_index_batch(@bitsPerDim, @batchSize)";

            var results = await connection.QueryAsync<HilbertResult>(
                new CommandDefinition(
                    sql,
                    new { bitsPerDim = bitsPerDimension, batchSize },
                    cancellationToken: cancellationToken));

            var dictionary = results.ToDictionary(r => r.ConstantId, r => r.HilbertIndex);
            
            _logger.LogInformation(
                "GPU Hilbert indexing computed {Count} indices (bits={Bits}, batch={Batch})",
                dictionary.Count, bitsPerDimension, batchSize);

            return dictionary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing GPU Hilbert indices");
            throw;
        }
    }

    public async Task<GpuCapabilities> CheckGpuAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        // Return cached result if available
        if (_cachedCapabilities is not null)
            return _cachedCapabilities;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = "SELECT * FROM gpu_check_availability()";

            var result = await connection.QuerySingleAsync<GpuCapabilityDto>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));

            _cachedCapabilities = new GpuCapabilities
            {
                HasCuPy = result.HasCupy,
                HasCuMl = result.HasCuml,
                GpuCount = result.GpuCount,
                GpuMemoryMb = result.GpuMemoryMb,
                ErrorMessage = result.ErrorMessage
            };

            _logger.LogInformation(
                "GPU availability check: CuPy={HasCuPy}, cuML={HasCuMl}, GPUs={GpuCount}, Memory={MemoryMb}MB, Error={Error}",
                _cachedCapabilities.HasCuPy,
                _cachedCapabilities.HasCuMl,
                _cachedCapabilities.GpuCount,
                _cachedCapabilities.GpuMemoryMb,
                _cachedCapabilities.ErrorMessage ?? "None");

            return _cachedCapabilities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking GPU availability");
            
            // Return unavailable capabilities on error
            _cachedCapabilities = new GpuCapabilities
            {
                HasCuPy = false,
                HasCuMl = false,
                GpuCount = 0,
                GpuMemoryMb = 0,
                ErrorMessage = ex.Message
            };

            return _cachedCapabilities;
        }
    }

    public async Task<IReadOnlyList<LandmarkCandidate>> DetectLandmarksAsync(
        double epsilon,
        int minSamples,
        int minClusterSize,
        CancellationToken cancellationToken = default)
    {
        if (epsilon <= 0) throw new ArgumentOutOfRangeException(nameof(epsilon));
        if (minSamples <= 0) throw new ArgumentOutOfRangeException(nameof(minSamples));
        if (minClusterSize <= 0) throw new ArgumentOutOfRangeException(nameof(minClusterSize));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = @"
                SELECT cluster_id, centroid_x, centroid_y, centroid_z, member_count, suggested_name
                FROM detect_landmarks_from_clustering(@epsilon, @minSamples, @minClusterSize)
                ORDER BY member_count DESC";

            var results = await connection.QueryAsync<LandmarkDto>(
                new CommandDefinition(
                    sql,
                    new { epsilon, minSamples, minClusterSize },
                    cancellationToken: cancellationToken));

            var list = results.Select(r => new LandmarkCandidate
            {
                ClusterId = r.ClusterId,
                Centroid = SpatialCoordinate.Create(r.CentroidX, r.CentroidY, r.CentroidZ),
                MemberCount = r.MemberCount,
                SuggestedName = r.SuggestedName
            }).ToList();

            _logger.LogInformation(
                "Landmark detection found {Count} candidates (eps={Epsilon:F3}, minSamples={MinSamples}, minSize={MinSize})",
                list.Count, epsilon, minSamples, minClusterSize);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting landmarks");
            throw;
        }
    }

    // DTOs for Dapper mapping
    private sealed record KnnResult
    {
        public Guid ConstantId { get; init; }
        public double Distance { get; init; }
    }

    private sealed record ClusterResult
    {
        public Guid ConstantId { get; init; }
        public int ClusterId { get; init; }
    }

    private sealed record SimilarityResult
    {
        public Guid ConstantId { get; init; }
        public double Similarity { get; init; }
    }

    private sealed record BpeResult
    {
        public byte[] BytePair { get; init; } = Array.Empty<byte>();
        public int Frequency { get; init; }
    }

    private sealed record HilbertResult
    {
        public Guid ConstantId { get; init; }
        public ulong HilbertIndex { get; init; }
    }

    private sealed record GpuCapabilityDto
    {
        public bool HasCupy { get; init; }
        public bool HasCuml { get; init; }
        public int GpuCount { get; init; }
        public long GpuMemoryMb { get; init; }
        public string? ErrorMessage { get; init; }
    }

    private sealed record LandmarkDto
    {
        public int ClusterId { get; init; }
        public double CentroidX { get; init; }
        public double CentroidY { get; init; }
        public double CentroidZ { get; init; }
        public int MemberCount { get; init; }
        public string SuggestedName { get; init; } = string.Empty;
    }
}
