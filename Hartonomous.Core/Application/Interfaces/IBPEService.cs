using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Service for Byte Pair Encoding (BPE) tokenization and learning
/// Discovers frequent byte patterns and creates hierarchical compositions
/// </summary>
public interface IBPEService
{
    /// <summary>
    /// Learn BPE vocabulary from a collection of constants
    /// Identifies most frequent pairs and creates composite tokens
    /// </summary>
    /// <param name="constants">Constants to analyze for patterns</param>
    /// <param name="maxVocabularySize">Maximum number of tokens to learn</param>
    /// <param name="minFrequency">Minimum frequency for a pair to be considered</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Learned BPE tokens ordered by frequency</returns>
    Task<List<BPEToken>> LearnVocabularyAsync(
        IEnumerable<Constant> constants, 
        int maxVocabularySize = 10000, 
        int minFrequency = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Encode byte sequence into BPE tokens using existing vocabulary
    /// </summary>
    /// <param name="data">Raw byte data to tokenize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sequence of BPE token IDs</returns>
    Task<List<int>> EncodeAsync(
        byte[] data, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decode BPE token sequence back to original bytes
    /// </summary>
    /// <param name="tokenIds">BPE token IDs to decode</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Original byte data</returns>
    Task<byte[]> DecodeAsync(
        List<int> tokenIds, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get compression ratio for given data using current vocabulary
    /// </summary>
    /// <param name="data">Data to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Compression ratio (original size / tokenized size)</returns>
    Task<double> GetCompressionRatioAsync(
        byte[] data, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find most similar sequences to given token based on spatial proximity
    /// </summary>
    /// <param name="tokenId">Token to find similar sequences for</param>
    /// <param name="k">Number of similar sequences to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Similar BPE tokens ordered by spatial distance</returns>
    Task<List<BPEToken>> FindSimilarTokensAsync(
        int tokenId, 
        int k = 10, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh BPE vocabulary by relearning from all constants
    /// Should be run periodically as new content is ingested
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of new tokens learned</returns>
    Task<int> RefreshVocabularyAsync(CancellationToken cancellationToken = default);
}
