using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data.Repositories;

/// <summary>
/// Repository implementation for BPEToken entity
/// </summary>
public class BPETokenRepository : Repository<BPEToken>, IBPETokenRepository
{
    public BPETokenRepository(ApplicationDbContext context) : base(context)
    {
    }
    
    public async Task<BPEToken?> GetByTokenIdAsync(int tokenId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(t => t.Constants)
            .FirstOrDefaultAsync(t => t.TokenId == tokenId, cancellationToken);
    }
    
    public async Task<BPEToken?> GetByHashAsync(Hash256 hash, CancellationToken cancellationToken = default)
    {
        if (hash == null)
        {
            throw new ArgumentNullException(nameof(hash));
        }
        
        return await _dbSet
            .Include(t => t.Constants)
            .FirstOrDefaultAsync(t => t.Hash == hash, cancellationToken);
    }
    
    public async Task<IEnumerable<BPEToken>> GetActiveTokensAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.IsActive)
            .OrderBy(t => t.TokenId)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<BPEToken>> GetByMergeLevelAsync(
        int mergeLevel,
        CancellationToken cancellationToken = default)
    {
        if (mergeLevel < 0)
        {
            throw new ArgumentException("Merge level cannot be negative", nameof(mergeLevel));
        }
        
        return await _dbSet
            .Where(t => t.MergeLevel == mergeLevel)
            .Where(t => t.IsActive)
            .OrderBy(t => t.TokenId)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<BPEToken>> GetTopByFrequencyAsync(
        int count = 100,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            throw new ArgumentException("Count must be positive", nameof(count));
        }
        
        return await _dbSet
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.Frequency)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<BPEToken>> GetByVocabularyRankAsync(
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentException("Page number must be >= 1", nameof(pageNumber));
        }
        
        if (pageSize < 1 || pageSize > 1000)
        {
            throw new ArgumentException("Page size must be between 1 and 1000", nameof(pageSize));
        }
        
        return await _dbSet
            .Where(t => t.IsActive)
            .OrderBy(t => t.VocabularyRank)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<BPEToken>> GetBySequenceLengthAsync(
        int minLength,
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        if (minLength < 1 || maxLength < minLength)
        {
            throw new ArgumentException("Invalid sequence length range");
        }
        
        return await _dbSet
            .Where(t => t.SequenceLength >= minLength && t.SequenceLength <= maxLength)
            .Where(t => t.IsActive)
            .OrderBy(t => t.SequenceLength)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<BPEToken>> GetStaleTokensAsync(
        DateTime olderThan,
        int maxResults = 1000,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.LastUsedAt < olderThan)
            .Where(t => t.IsActive)
            .OrderBy(t => t.LastUsedAt)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<int> GetNextTokenIdAsync(CancellationToken cancellationToken = default)
    {
        var maxTokenId = await _dbSet
            .MaxAsync(t => (int?)t.TokenId, cancellationToken);
        
        return (maxTokenId ?? -1) + 1;
    }
    
    public async Task<bool> ExistsByTokenIdAsync(int tokenId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(t => t.TokenId == tokenId, cancellationToken);
    }
    
    public async Task<IEnumerable<BPEToken>> GetTokensContainingConstantAsync(
        Guid constantId,
        CancellationToken cancellationToken = default)
    {
        if (constantId == Guid.Empty)
        {
            throw new ArgumentException("Constant ID cannot be empty", nameof(constantId));
        }
        
        return await _dbSet
            .Where(t => t.ConstantSequence.Contains(constantId))
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<int> GetVocabularySizeAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.IsActive)
            .CountAsync(cancellationToken);
    }

    public async Task<int> GetMaxTokenIdAsync(CancellationToken cancellationToken = default)
    {
        var maxTokenId = await _dbSet
            .MaxAsync(t => (int?)t.TokenId, cancellationToken);
        
        return maxTokenId ?? 0;
    }

    public async Task DeleteCompositeTokensAsync(CancellationToken cancellationToken = default)
    {
        // Delete all tokens with merge level > 0 (composite tokens)
        var compositeTokens = await _dbSet
            .Where(t => t.MergeLevel > 0)
            .ToListAsync(cancellationToken);

        _dbSet.RemoveRange(compositeTokens);
        // Note: SaveChanges must be called by UnitOfWork
    }

    /// <summary>
    /// Find k-nearest tokens based on geometric similarity (LINESTRINGZM distance)
    /// </summary>
    public async Task<List<BPEToken>> GetNearestNeighborsAsync(
        BPEToken sourceToken, 
        int k, 
        CancellationToken cancellationToken = default)
    {
        if (sourceToken == null || sourceToken.CompositionGeometry == null)
            return new List<BPEToken>();

        // Use PostGIS distance query on the CompositionGeometry (LINESTRINGZM)
        // We exclude the source token itself
        return await _dbSet
            .Where(t => t.Id != sourceToken.Id && t.CompositionGeometry != null)
            .OrderBy(t => t.CompositionGeometry!.Distance(sourceToken.CompositionGeometry))
            .Take(k)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get all tokens (including inactive)
    /// </summary>
    public new async Task<List<BPEToken>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.IgnoreQueryFilters().ToListAsync(cancellationToken);
    }
}
