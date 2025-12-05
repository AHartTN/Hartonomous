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

    public async Task<List<int>> EncodeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
            return new List<int>();

        // 1. Atomize: Convert bytes to initial sequence of constant hashes
        // For simplicity in this "Universal" BPE, we assume input is a stream of byte-constants?
        // Or are we matching against existing 1-byte constants?
        // BPE usually starts with character vocabulary.
        // Here, our "atoms" are Constants.
        // We need to decompose data into smallest known constants first.
        // Assuming single-byte constants exist for all 256 bytes.
        
        var sequence = new List<int>(); // Token IDs
        
        // Optimistic: Try to find single-byte tokens first
        // If not found, we'd need to create them or fail.
        // Assuming vocabulary includes base alphabet (single bytes).
        
        // Fetch base vocabulary (tokens for single bytes/atoms)
        // This would be slow if not cached. For now, we fetch as needed or assume 0-255 are reserved?
        // Let's compute hashes for single bytes.
        
        var currentTokens = new List<int>();
        foreach (var b in data)
        {
            var singleByte = new byte[] { b };
            var hash = Hash256.Compute(singleByte);
            var token = await _tokenRepository.GetByHashAsync(hash, cancellationToken);
            if (token != null)
            {
                currentTokens.Add(token.TokenId);
            }
            else
            {
                // Fallback or create? For encoding, we usually expect base vocab to exist.
                // If not, we can't encode this byte.
                _logger.LogWarning("Base token not found for byte {Byte}", b);
                // Use a special UNK token or skip?
                currentTokens.Add(-1); 
            }
        }

        // 2. BPE Merge Loop
        bool merged = true;
        while (merged)
        {
            merged = false;
            int bestPairIndex = -1;
            BPEToken? bestToken = null;
            long bestRank = -1;

            // Find best pair to merge
            for (int i = 0; i < currentTokens.Count - 1; i++)
            {
                var leftId = currentTokens[i];
                var rightId = currentTokens[i + 1];
                
                if (leftId == -1 || rightId == -1) continue;

                // Check if pair (left, right) exists in vocabulary
                // This is the "slow" part without a memory-cached vocab map (Pair -> TokenId)
                // We'll try to query the repo for a token that has these parents?
                // Or compute the combined hash (if we had the data).
                // But we only have IDs here.
                
                // Ideally, _tokenRepository should support GetByParentIds(left, right)
                // or we compute the hash from the *actual* data of the tokens.
                
                // Optimized approach: We need the data or hash of the *combined* token to lookup.
                // But we don't have it easily without expanding.
                
                // ALTERNATIVE: Retrieve all potential merge candidates for the current set of tokens?
                // This implementation is naive without an in-memory vocabulary trie/map.
                // Given the constraints, we'll assume we can lookup by (LeftId, RightId) if supported,
                // OR we have to reconstruction the data to hash it.
                
                // Let's reconstruct data for the pair to check the hash
                // (Expensive, but correct)
                var d1 = await DecodeAsync(new List<int> { leftId }, cancellationToken);
                var d2 = await DecodeAsync(new List<int> { rightId }, cancellationToken);
                var combined = d1.Concat(d2).ToArray();
                var hash = Hash256.Compute(combined);
                
                var token = await _tokenRepository.GetByHashAsync(hash, cancellationToken);
                if (token != null)
                {
                    // Use Frequency or Rank to decide priority
                    // Higher frequency = better merge
                    if (token.Frequency > bestRank)
                    {
                        bestRank = token.Frequency;
                        bestPairIndex = i;
                        bestToken = token;
                        merged = true;
                    }
                }
            }

            if (merged && bestToken != null)
            {
                // Apply merge
                currentTokens[bestPairIndex] = bestToken.TokenId;
                currentTokens.RemoveAt(bestPairIndex + 1);
                // Restart scan or continue? Standard BPE restarts or handles non-overlapping.
                // Restarting is safer for correctness.
            }
        }

        return currentTokens;
    }

    public async Task<byte[]> DecodeAsync(List<int> tokens, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        foreach (var tokenId in tokens)
        {
            var token = await _tokenRepository.GetByTokenIdAsync(tokenId, cancellationToken);
            if (token == null) continue;

            // If token has raw data (it's a constant/atom), write it.
            // If it's a composite, we might store the full data or need to recurse?
            // BPEToken entity doesn't explicitly store 'Data' for composites, only 'ConstantSequence'.
            // But 'Constant' entity has Data.
            
            // Recursive expansion
            if (token.ConstantSequence != null && token.ConstantSequence.Any())
            {
                foreach (var constantId in token.ConstantSequence)
                {
                    var constant = await _constantRepository.GetByIdAsync(constantId, cancellationToken);
                    if (constant != null)
                    {
                        await ms.WriteAsync(constant.Data, cancellationToken);
                    }
                }
            }
        }
        return ms.ToArray();
    }

    public async Task<double> GetCompressionRatioAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var encoded = await EncodeAsync(data, cancellationToken);
        if (encoded.Count == 0) return 0;
        
        // Ratio = Original Size / Compressed Size
        // Compressed size = number of tokens * 4 bytes (int)
        // (Rough approximation)
        return (double)data.Length / (encoded.Count * 4);
    }

    public async Task<List<BPEToken>> FindSimilarTokensAsync(int tokenId, int topK = 10, CancellationToken cancellationToken = default)
    {
        var sourceToken = await _tokenRepository.GetByTokenIdAsync(tokenId, cancellationToken);
        if (sourceToken == null || sourceToken.CompositionGeometry == null)
            return new List<BPEToken>();

        // Geometric similarity search using the CompositionGeometry (LINESTRINGZM)
        // We find tokens with spatially similar paths
        // This requires a spatial query supported by the repository
        
        return await _tokenRepository.GetNearestNeighborsAsync(sourceToken, topK, cancellationToken);
    }

    public Task<int> RefreshVocabularyAsync(CancellationToken cancellationToken = default)
    {
        // Reload in-memory vocabulary cache (if implemented)
        return Task.FromResult(0);
    }
}
