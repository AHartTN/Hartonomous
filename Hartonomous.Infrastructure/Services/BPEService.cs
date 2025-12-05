using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Infrastructure.Services.BPE;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Services;

/// <summary>
/// Geometric BPE service using Hilbert-sorted sequences and MST-based vocabulary learning
/// </summary>
public class BPEService : IBPEService
{
    private readonly IConstantRepository _constantRepository;
    private readonly IBPETokenRepository _tokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BPEService> _logger;
    private readonly VoronoiTessellator _voronoiTessellator;
    private readonly MinimumSpanningTreeComputer _mstComputer;

    public BPEService(
        IConstantRepository constantRepository,
        IBPETokenRepository tokenRepository,
        IUnitOfWork unitOfWork,
        ILogger<BPEService> logger,
        VoronoiTessellator voronoiTessellator,
        MinimumSpanningTreeComputer mstComputer)
    {
        _constantRepository = constantRepository;
        _tokenRepository = tokenRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _voronoiTessellator = voronoiTessellator;
        _mstComputer = mstComputer;
    }

    public async Task<List<BPEToken>> LearnVocabularyAsync(
        IEnumerable<Constant> constants,
        int maxVocabularySize = 10000,
        int minFrequency = 2,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Learning BPE vocabulary (maxSize={MaxSize}, minFreq={MinFreq})",
            maxVocabularySize, minFrequency);

        cancellationToken.ThrowIfCancellationRequested();

        // Step 1: Sort constants by Hilbert index and remove duplicates
        var sortedConstants = constants
            .Where(c => c.Coordinate != null)
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .OrderBy(c => c.Coordinate!.HilbertHigh)
            .ThenBy(c => c.Coordinate!.HilbertLow)
            .ToList();

        if (sortedConstants.Count < 2)
        {
            _logger.LogWarning("Insufficient constants for BPE: {Count}", sortedConstants.Count);
            return new List<BPEToken>();
        }

        _logger.LogDebug("Processing {Count} Hilbert-sorted constants", sortedConstants.Count);

        cancellationToken.ThrowIfCancellationRequested();

        // Step 2: Detect gaps in Hilbert sequence
        var gaps = HilbertGapDetector.DetectGaps(sortedConstants, gapThreshold: 1000, _logger);
        _logger.LogDebug("Detected {GapCount} gaps in Hilbert sequence", gaps.Count);

        // Step 3: Build dense segments
        var segments = HilbertGapDetector.BuildSegments(sortedConstants, gaps);
        _logger.LogDebug("Created {SegmentCount} dense segments", segments.Count);

        cancellationToken.ThrowIfCancellationRequested();

        // Step 4: Compute Voronoi neighbors per segment
        var allPairs = new List<ConstantPair>();
        foreach (var segment in segments.Where(s => s.Count > 1))
        {
            var pairs = await _voronoiTessellator.ComputeNeighborsAsync(segment, cancellationToken);
            allPairs.AddRange(pairs);
            cancellationToken.ThrowIfCancellationRequested();
        }
        _logger.LogDebug("Found {PairCount} Voronoi neighbor pairs", allPairs.Count);

        // Step 5: Build graph from pairs
        var graph = new Graph();
        foreach (var pair in allPairs)
        {
            graph.AddEdge(pair.ConstantId1, pair.ConstantId2, pair.Distance3D, pair.HilbertDistance);
        }

        // Step 6: Compute MST
        var mst = _mstComputer.ComputeMST(graph);
        _logger.LogDebug("MST has {EdgeCount} edges", mst.Edges.Count);

        cancellationToken.ThrowIfCancellationRequested();

        // Step 7: Select vocabulary from MST edges
        var vocabulary = await SelectVocabularyFromMSTAsync(
            mst,
            sortedConstants,
            maxVocabularySize,
            minFrequency,
            cancellationToken);

        _logger.LogInformation("Learned {VocabSize} BPE tokens", vocabulary.Count);
        return vocabulary;
    }

    private async Task<List<BPEToken>> SelectVocabularyFromMSTAsync(
        Graph mst,
        List<Constant> constants,
        int maxVocabularySize,
        int minFrequency,
        CancellationToken cancellationToken)
    {
        var constantLookup = constants.ToDictionary(c => c.Id);
        var vocabulary = new List<BPEToken>();
        var nextTokenId = await _tokenRepository.GetMaxTokenIdAsync(cancellationToken) + 1;

        // Sort MST edges by Hilbert distance (prefer spatially close pairs)
        var sortedEdges = mst.Edges
            .OrderBy(e => e.HilbertDistance)
            .Take(maxVocabularySize)
            .ToList();

        foreach (var edge in sortedEdges)
        {
            if (!constantLookup.TryGetValue(edge.Vertex1, out var c1) ||
                !constantLookup.TryGetValue(edge.Vertex2, out var c2))
                continue;

            // Compute hash of merged pair
            var mergedData = c1.Data.Concat(c2.Data).ToArray();
            var hash = Hash256.Compute(mergedData);

            // Check if token already exists
            var existingToken = await _tokenRepository.GetByHashAsync(hash, cancellationToken);
            if (existingToken != null)
            {
                existingToken.IncrementFrequency();
                continue;
            }

            // Create new BPE token
            var token = BPEToken.CreateFromConstantSequence(
                tokenId: nextTokenId++,
                constantSequence: new List<Guid> { c1.Id, c2.Id },
                hash: hash,
                mergeLevel: 1,
                constants: new List<Constant> { c1, c2 });

            vocabulary.Add(token);
            await _tokenRepository.AddAsync(token, cancellationToken);

            if (vocabulary.Count >= maxVocabularySize)
                break;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return vocabulary;
    }

    public Task<List<int>> EncodeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Encoding not yet implemented in geometric BPE");
    }

    public Task<byte[]> DecodeAsync(List<int> tokens, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Decoding not yet implemented in geometric BPE");
    }

    public Task<double> GetCompressionRatioAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Compression ratio not yet implemented in geometric BPE");
    }

    public Task<List<BPEToken>> FindSimilarTokensAsync(int tokenId, int topK = 10, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Similar tokens not yet implemented in geometric BPE");
    }

    public Task<int> RefreshVocabularyAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Vocabulary refresh not yet implemented in geometric BPE");
    }
}
