using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Repository interface for BPEToken entity
/// </summary>
public interface IBPETokenRepository : IRepository<BPEToken>
{
    /// <summary>
    /// Get token by its token ID
    /// </summary>
    Task<BPEToken?> GetByTokenIdAsync(int tokenId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get token by hash
    /// </summary>
    Task<BPEToken?> GetByHashAsync(Hash256 hash, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all active tokens
    /// </summary>
    Task<IEnumerable<BPEToken>> GetActiveTokensAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get tokens by merge level
    /// </summary>
    Task<IEnumerable<BPEToken>> GetByMergeLevelAsync(
        int mergeLevel,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get top tokens by frequency
    /// </summary>
    Task<IEnumerable<BPEToken>> GetTopByFrequencyAsync(
        int count = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get tokens ordered by vocabulary rank
    /// </summary>
    Task<IEnumerable<BPEToken>> GetByVocabularyRankAsync(
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get tokens by sequence length
    /// </summary>
    Task<IEnumerable<BPEToken>> GetBySequenceLengthAsync(
        int minLength,
        int maxLength,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get tokens that haven't been used recently
    /// </summary>
    Task<IEnumerable<BPEToken>> GetStaleTokensAsync(
        DateTime olderThan,
        int maxResults = 1000,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get next available token ID
    /// </summary>
    Task<int> GetNextTokenIdAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if token ID exists
    /// </summary>
    Task<bool> ExistsByTokenIdAsync(int tokenId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get tokens containing specific constant
    /// </summary>
    Task<IEnumerable<BPEToken>> GetTokensContainingConstantAsync(
        Guid constantId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get total number of active tokens (vocabulary size)
    /// </summary>
    Task<int> GetVocabularySizeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get maximum token ID currently in use
    /// </summary>
    Task<int> GetMaxTokenIdAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete all composite tokens (merge level > 0), keeping only base tokens
    /// </summary>
    Task DeleteCompositeTokensAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all tokens (including inactive)
    /// </summary>
    new Task<List<BPEToken>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Find k-nearest tokens based on geometric similarity (LINESTRINGZM distance)
    /// </summary>
    Task<List<BPEToken>> GetNearestNeighborsAsync(
        BPEToken sourceToken, 
        int k, 
        CancellationToken cancellationToken = default);
}
