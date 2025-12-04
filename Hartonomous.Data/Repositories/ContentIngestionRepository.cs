using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data.Repositories;

/// <summary>
/// Repository implementation for ContentIngestion entity
/// </summary>
public class ContentIngestionRepository : Repository<ContentIngestion>, IContentIngestionRepository
{
    public ContentIngestionRepository(ApplicationDbContext context) : base(context)
    {
    }
    
    public async Task<ContentIngestion?> GetByContentHashAsync(Hash256 hash, CancellationToken cancellationToken = default)
    {
        if (hash == null)
        {
            throw new ArgumentNullException(nameof(hash));
        }
        
        return await _dbSet
            .FirstOrDefaultAsync(i => i.ContentHash == hash, cancellationToken);
    }
    
    public async Task<IEnumerable<ContentIngestion>> GetBySourceAsync(
        string sourceIdentifier,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentifier))
        {
            throw new ArgumentException("Source identifier cannot be null or empty", nameof(sourceIdentifier));
        }
        
        return await _dbSet
            .Where(i => i.SourceIdentifier == sourceIdentifier)
            .OrderByDescending(i => i.StartedAt)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<ContentIngestion>> GetByContentTypeAsync(
        ContentType contentType,
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
            .Where(i => i.ContentType == contentType)
            .OrderByDescending(i => i.StartedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<ContentIngestion>> GetRecentIngestionsAsync(
        int count = 100,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            throw new ArgumentException("Count must be positive", nameof(count));
        }
        
        return await _dbSet
            .OrderByDescending(i => i.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<ContentIngestion>> GetFailedIngestionsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Where(i => !i.IsSuccessful);
        
        if (since.HasValue)
        {
            query = query.Where(i => i.StartedAt >= since.Value);
        }
        
        return await query
            .OrderByDescending(i => i.StartedAt)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<(int TotalIngestions, int Successful, int Failed, long TotalBytes)> GetStatisticsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();
        
        if (since.HasValue)
        {
            query = query.Where(i => i.StartedAt >= since.Value);
        }
        
        var totalIngestions = await query.CountAsync(cancellationToken);
        var successful = await query.CountAsync(i => i.IsSuccessful, cancellationToken);
        var failed = await query.CountAsync(i => !i.IsSuccessful, cancellationToken);
        var totalBytes = await query.SumAsync(i => i.OriginalSize, cancellationToken);
        
        return (totalIngestions, successful, failed, totalBytes);
    }
    
    public async Task<double> GetAverageDeduplicationRatioAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Where(i => i.IsSuccessful);
        
        if (since.HasValue)
        {
            query = query.Where(i => i.StartedAt >= since.Value);
        }
        
        var avgRatio = await query.AverageAsync(i => (double?)i.DeduplicationRatio, cancellationToken);
        
        return avgRatio ?? 0.0;
    }
    
    public async Task<bool> HasBeenIngestedAsync(Hash256 hash, CancellationToken cancellationToken = default)
    {
        if (hash == null)
        {
            throw new ArgumentNullException(nameof(hash));
        }
        
        return await _dbSet.AnyAsync(i => i.ContentHash == hash && i.IsSuccessful, cancellationToken);
    }
    
    public async Task<long> GetTotalIngestionsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.LongCountAsync(cancellationToken);
    }
}
