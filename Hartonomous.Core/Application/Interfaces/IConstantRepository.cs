using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Repository interface for Constant entity with spatial query support
/// </summary>
public interface IConstantRepository : IRepository<Constant>
{
    /// <summary>
    /// Find constant by its hash
    /// </summary>
    Task<Constant?> GetByHashAsync(Hash256 hash, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find constant by hash string
    /// </summary>
    Task<Constant?> GetByHashStringAsync(string hashHex, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find constants within a spatial radius of a coordinate
    /// </summary>
    Task<IEnumerable<Constant>> GetNearbyConstantsAsync(
        SpatialCoordinate center,
        double radius,
        int maxResults = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find k-nearest constants to a coordinate
    /// </summary>
    Task<IEnumerable<Constant>> GetKNearestConstantsAsync(
        SpatialCoordinate center,
        int k = 10,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get constants by status
    /// </summary>
    Task<IEnumerable<Constant>> GetByStatusAsync(
        ConstantStatus status,
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find constants by Hilbert ID range (spatial locality)
    /// </summary>
    Task<IEnumerable<Constant>> GetByHilbertRangeAsync(
        ulong startId,
        ulong endId,
        int maxResults = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get most frequently accessed constants
    /// </summary>
    Task<IEnumerable<Constant>> GetTopByFrequencyAsync(
        int count = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get constants that haven't been accessed recently
    /// </summary>
    Task<IEnumerable<Constant>> GetStaleConstantsAsync(
        DateTime olderThan,
        int maxResults = 1000,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if constant with hash already exists
    /// </summary>
    Task<bool> ExistsByHashAsync(Hash256 hash, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get total storage size occupied by all constants
    /// </summary>
    Task<long> GetTotalStorageSizeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get deduplication statistics
    /// </summary>
    Task<(int TotalConstants, int UniqueConstants, double DeduplicationRatio)> GetDeduplicationStatsAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get total count of all constants
    /// </summary>
    Task<long> GetTotalConstantsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get count of active constants
    /// </summary>
    Task<long> GetActiveConstantsCountAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all active constants (use with caution - may be large dataset)
    /// </summary>
    Task<List<Constant>> GetAllActiveAsync(CancellationToken cancellationToken = default);
}
