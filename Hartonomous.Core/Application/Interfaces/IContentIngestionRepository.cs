using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Repository interface for ContentIngestion entity
/// </summary>
public interface IContentIngestionRepository : IRepository<ContentIngestion>
{
    /// <summary>
    /// Get ingestion by content hash
    /// </summary>
    Task<ContentIngestion?> GetByContentHashAsync(Hash256 hash, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get ingestions by source identifier
    /// </summary>
    Task<IEnumerable<ContentIngestion>> GetBySourceAsync(
        string sourceIdentifier,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get ingestions by content type
    /// </summary>
    Task<IEnumerable<ContentIngestion>> GetByContentTypeAsync(
        ContentType contentType,
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get recent ingestions
    /// </summary>
    Task<IEnumerable<ContentIngestion>> GetRecentIngestionsAsync(
        int count = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get failed ingestions
    /// </summary>
    Task<IEnumerable<ContentIngestion>> GetFailedIngestionsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get ingestion statistics
    /// </summary>
    Task<(int TotalIngestions, int Successful, int Failed, long TotalBytes)> GetStatisticsAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get average deduplication ratio
    /// </summary>
    Task<double> GetAverageDeduplicationRatioAsync(
        DateTime? since = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if content hash has been ingested before
    /// </summary>
    Task<bool> HasBeenIngestedAsync(Hash256 hash, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get total count of all ingestions
    /// </summary>
    Task<long> GetTotalIngestionsAsync(CancellationToken cancellationToken = default);
}
