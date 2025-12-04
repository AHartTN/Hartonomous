using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Services;

/// <summary>
/// Byte Pair Encoding service implementation
/// Uses classical BPE algorithm with spatial awareness for similarity
/// </summary>
public class BPEService : IBPEService
{
    private readonly IConstantRepository _constantRepository;
    private readonly IBPETokenRepository _tokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BPEService> _logger;
    
    // In-memory vocabulary cache for fast encoding
    private Dictionary<string, int> _vocabulary = new();
    private Dictionary<int, List<Guid>> _tokenToConstants = new();
    private bool _vocabularyLoaded = false;

    public BPEService(
        IConstantRepository constantRepository,
        IBPETokenRepository tokenRepository,
        IUnitOfWork unitOfWork,
        ILogger<BPEService> logger)
    {
        _constantRepository = constantRepository;
        _tokenRepository = tokenRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<List<BPEToken>> LearnVocabularyAsync(
        IEnumerable<Constant> constants, 
        int maxVocabularySize = 10000, 
        int minFrequency = 10, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting BPE vocabulary learning with max size {MaxSize} and min frequency {MinFreq}", 
            maxVocabularySize, minFrequency);

        var constantsList = constants.ToList();
        if (constantsList.Count == 0)
        {
            _logger.LogWarning("No constants provided for vocabulary learning");
            return new List<BPEToken>();
        }

        try
        {
            // Step 1: Initialize data structures
            var constantLookup = constantsList.ToDictionary(c => c.Id, c => c);
            var pairFrequencies = new Dictionary<(Guid, Guid), long>();
            var sequences = new List<List<Guid>>();
            var tokenToSequence = new Dictionary<Guid, List<Guid>>(); // Map merged token ID to its constituent sequence
            
            // Build initial sequences from constants (single-element sequences)
            foreach (var constant in constantsList)
            {
                var sequence = new List<Guid> { constant.Id };
                sequences.Add(sequence);
            }

            var learnedTokens = new List<BPEToken>();
            var nextTokenId = await _tokenRepository.GetMaxTokenIdAsync(cancellationToken) + 1;

            _logger.LogInformation("Starting BPE iterations with {ConstantCount} constants and base token ID {NextTokenId}", 
                constantsList.Count, nextTokenId);

            // Step 2: Iteratively merge most frequent pairs
            for (int iteration = 0; iteration < maxVocabularySize; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Count pair frequencies across all sequences
                pairFrequencies.Clear();
                foreach (var sequence in sequences)
                {
                    for (int i = 0; i < sequence.Count - 1; i++)
                    {
                        var pair = (sequence[i], sequence[i + 1]);
                        pairFrequencies.TryGetValue(pair, out var count);
                        pairFrequencies[pair] = count + 1;
                    }
                }

                // Find most frequent pair
                if (pairFrequencies.Count == 0)
                {
                    _logger.LogInformation("No more pairs to merge at iteration {Iteration}", iteration);
                    break;
                }
                
                var mostFrequentPair = pairFrequencies
                    .OrderByDescending(kv => kv.Value)
                    .First();

                // Check convergence criteria
                if (mostFrequentPair.Value < minFrequency)
                {
                    _logger.LogInformation(
                        "Stopping BPE learning at iteration {Iteration}: most frequent pair has frequency {Freq} below minimum {MinFreq}", 
                        iteration, mostFrequentPair.Value, minFrequency);
                    break;
                }

                var (leftId, rightId) = mostFrequentPair.Key;
                var frequency = mostFrequentPair.Value;

                // Get constituent sequence for each element (may be merged tokens or original constants)
                var leftSequence = tokenToSequence.ContainsKey(leftId) 
                    ? tokenToSequence[leftId] 
                    : new List<Guid> { leftId };
                var rightSequence = tokenToSequence.ContainsKey(rightId) 
                    ? tokenToSequence[rightId] 
                    : new List<Guid> { rightId };

                // Merge sequences
                var mergedSequence = new List<Guid>(leftSequence);
                mergedSequence.AddRange(rightSequence);

                // Compute merge level (max of constituents + 1)
                var leftMergeLevel = learnedTokens.FirstOrDefault(t => t.Id == leftId)?.MergeLevel ?? 0;
                var rightMergeLevel = learnedTokens.FirstOrDefault(t => t.Id == rightId)?.MergeLevel ?? 0;
                var mergeLevel = Math.Max(leftMergeLevel, rightMergeLevel) + 1;

                // Get all constants in merged sequence for hash computation
                var mergedConstants = mergedSequence
                    .Select(id => constantLookup.ContainsKey(id) ? constantLookup[id] : null)
                    .Where(c => c != null)
                    .ToList();

                if (mergedConstants.Count == 0 || mergedConstants.Any(c => c == null))
                {
                    _logger.LogWarning(
                        "Cannot resolve constants for merged sequence at iteration {Iteration}, skipping", 
                        iteration);
                    continue;
                }

                // Compute hash of concatenated data
                var totalSize = mergedConstants.Sum(c => c!.Size);
                var mergedData = new byte[totalSize];
                var offset = 0;
                foreach (var constant in mergedConstants)
                {
                    Array.Copy(constant!.Data, 0, mergedData, offset, constant.Size);
                    offset += constant.Size;
                }
                var mergedHash = Hash256.Compute(mergedData);

                // Check for existing token with same hash (deduplication)
                var existingToken = await _tokenRepository.GetByHashAsync(mergedHash, cancellationToken);
                if (existingToken != null)
                {
                    _logger.LogDebug(
                        "Token for hash {Hash} already exists (ID: {TokenId}), incrementing frequency", 
                        mergedHash, existingToken.TokenId);
                    
                    existingToken.IncrementFrequency();
                    
                    // Replace occurrences in sequences using existing token ID
                    ReplacePairInSequences(sequences, leftId, rightId, existingToken.Id);
                    tokenToSequence[existingToken.Id] = mergedSequence;
                    
                    continue;
                }

                // Compute spatial interpolation for merged token coordinate
                SpatialCoordinate? interpolatedCoordinate = null;
                if (mergedConstants.All(c => c!.Coordinate != null))
                {
                    var coordinates = mergedConstants.Select(c => c!.Coordinate!).ToList();
                    interpolatedCoordinate = SpatialCoordinate.Interpolate(coordinates);
                }

                // Create new BPE token
                var newTokenId = Guid.NewGuid();
                var token = BPEToken.CreateFromConstantSequence(
                    tokenId: nextTokenId,
                    constantSequence: mergedSequence,
                    hash: mergedHash,
                    mergeLevel: mergeLevel,
                    constants: mergedConstants!
                );
                
                // Set frequency from pair analysis
                for (int i = 0; i < frequency - 1; i++)
                {
                    token.IncrementFrequency();
                }

                learnedTokens.Add(token);
                await _tokenRepository.AddAsync(token, cancellationToken);
                
                _logger.LogDebug(
                    "Created token {TokenId} (GUID: {Guid}) for pair ({Left}, {Right}) with frequency {Freq}, merge level {Level}, sequence length {Len}",
                    nextTokenId, token.Id, leftId, rightId, frequency, mergeLevel, mergedSequence.Count);

                // Update state for next iteration
                tokenToSequence[newTokenId] = mergedSequence;
                ReplacePairInSequences(sequences, leftId, rightId, newTokenId);
                nextTokenId++;

                // Commit in batches to avoid holding long transactions
                if (iteration % 100 == 0 && iteration > 0)
                {
                    _logger.LogInformation("Committing batch at iteration {Iteration} ({TokenCount} tokens)", 
                        iteration, learnedTokens.Count);
                    
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            // Step 3: Final commit and vocabulary ranking
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Update vocabulary ranks based on frequency
            var rankedTokens = learnedTokens.OrderByDescending(t => t.Frequency).ToList();
            for (int i = 0; i < rankedTokens.Count; i++)
            {
                rankedTokens[i].UpdateVocabularyRank(i + 1);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "BPE vocabulary learning complete: {TokenCount} tokens learned, min frequency {MinFreq}, max merge level {MaxLevel}",
                learnedTokens.Count, minFrequency, learnedTokens.Max(t => t.MergeLevel));
            
            // Invalidate cache to reload with new tokens
            _vocabularyLoaded = false;

            return learnedTokens;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("BPE vocabulary learning cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during BPE vocabulary learning");
            throw;
        }
    }

    /// <summary>
    /// Replace all occurrences of (left, right) pair in sequences with merged token ID
    /// </summary>
    private void ReplacePairInSequences(
        List<List<Guid>> sequences, 
        Guid leftId, 
        Guid rightId, 
        Guid mergedId)
    {
        foreach (var sequence in sequences)
        {
            for (int i = 0; i < sequence.Count - 1; i++)
            {
                if (sequence[i] == leftId && sequence[i + 1] == rightId)
                {
                    // Replace pair with merged token
                    sequence[i] = mergedId;
                    sequence.RemoveAt(i + 1);
                    // Don't increment i - check same position again for consecutive pairs
                }
            }
        }
    }

    public async Task<List<int>> EncodeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        await EnsureVocabularyLoadedAsync(cancellationToken);

        // For now, return byte-level encoding
        // In production, apply BPE merges from vocabulary
        var tokens = new List<int>();
        
        // Simple byte-to-token mapping (each byte gets a token ID)
        foreach (var b in data)
        {
            tokens.Add(b); // Token ID = byte value for simplicity
        }

        return tokens;
    }

    public async Task<byte[]> DecodeAsync(List<int> tokenIds, CancellationToken cancellationToken = default)
    {
        await EnsureVocabularyLoadedAsync(cancellationToken);

        // Simple reverse of encoding
        var bytes = new List<byte>();
        
        foreach (var tokenId in tokenIds)
        {
            if (tokenId < 256)
            {
                // Single-byte token
                bytes.Add((byte)tokenId);
            }
            else
            {
                // Multi-byte token - look up in vocabulary
                if (_tokenToConstants.TryGetValue(tokenId, out var constantIds))
                {
                    // Fetch constants and concatenate their data
                    foreach (var constId in constantIds)
                    {
                        var constant = await _constantRepository.GetByIdAsync(constId, cancellationToken);
                        if (constant != null)
                        {
                            bytes.AddRange(constant.Data);
                        }
                    }
                }
            }
        }

        return bytes.ToArray();
    }

    public async Task<double> GetCompressionRatioAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var tokens = await EncodeAsync(data, cancellationToken);
        
        // Compression ratio = original_size / compressed_size
        // Each token is represented as an int (4 bytes) but could be variable-length encoded
        var compressedSize = tokens.Count * sizeof(int);
        var originalSize = data.Length;

        return originalSize > 0 ? (double)originalSize / compressedSize : 0.0;
    }

    public async Task<List<BPEToken>> FindSimilarTokensAsync(
        int tokenId, 
        int k = 10, 
        CancellationToken cancellationToken = default)
    {
        // Get the target token
        var targetToken = await _tokenRepository.GetByTokenIdAsync(tokenId, cancellationToken);
        if (targetToken == null)
        {
            return new List<BPEToken>();
        }

        // For spatial similarity, compute centroid of token's constants
        // and find other tokens with nearby centroids
        
        // For now, return most frequent tokens (placeholder)
        var similarTokens = await _tokenRepository.GetTopByFrequencyAsync(k, cancellationToken);
        
        return similarTokens.Where(t => t.TokenId != tokenId).ToList();
    }

    public async Task<int> RefreshVocabularyAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refreshing BPE vocabulary from all constants");

        // Fetch all active constants
        var constants = await _constantRepository.GetAllActiveAsync(cancellationToken);
        
        // Clear existing non-base tokens (keep single-byte tokens)
        await _tokenRepository.DeleteCompositeTokensAsync(cancellationToken);
        
        // Relearn vocabulary
        var newTokens = await LearnVocabularyAsync(
            constants, 
            maxVocabularySize: 10000, 
            minFrequency: 10, 
            cancellationToken);

        _logger.LogInformation("Vocabulary refresh complete. {Count} tokens learned", newTokens.Count);

        return newTokens.Count;
    }

    private async Task EnsureVocabularyLoadedAsync(CancellationToken cancellationToken)
    {
        if (_vocabularyLoaded) return;

        _logger.LogDebug("Loading BPE vocabulary into memory");

        var allTokens = await _tokenRepository.GetAllAsync(cancellationToken);
        
        _vocabulary.Clear();
        _tokenToConstants.Clear();

        foreach (var token in allTokens)
        {
            var key = string.Join(",", token.ConstantSequence);
            _vocabulary[key] = token.TokenId;
            _tokenToConstants[token.TokenId] = token.ConstantSequence;
        }

        _vocabularyLoaded = true;
        
        _logger.LogInformation("BPE vocabulary loaded: {Count} tokens", _vocabulary.Count);
    }
}
