using Hartonomous.Core.Domain.Entities;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Repository interface for ContentBoundary entity with spatial overlap queries
/// </summary>
public interface IContentBoundaryRepository : IRepository<ContentBoundary>
{
    /// <summary>
    /// Get boundary by content ingestion ID
    /// </summary>
    Task<ContentBoundary?> GetByContentIngestionIdAsync(
        Guid contentIngestionId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find boundaries that intersect with target boundary
    /// </summary>
    Task<List<ContentBoundary>> FindIntersectingAsync(
        Polygon boundaryGeometry,
        double minOverlap = 0.0,
        int maxResults = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find most similar boundaries by Jaccard similarity
    /// </summary>
    Task<List<ContentBoundary>> FindSimilarAsync(
        Guid contentIngestionId,
        double minSimilarity = 0.5,
        int maxResults = 10,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find boundaries containing a specific point
    /// </summary>
    Task<List<ContentBoundary>> FindContainingPointAsync(
        Point location,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find boundaries within distance of point (by centroid)
    /// </summary>
    Task<List<ContentBoundary>> FindNearPointAsync(
        Point location,
        double maxDistance,
        int maxResults = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find densest boundaries (highest atom density)
    /// </summary>
    Task<List<ContentBoundary>> FindDensestAsync(
        int limit = 10,
        string? computationMethod = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find largest boundaries by area
    /// </summary>
    Task<List<ContentBoundary>> FindLargestAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find boundaries by computation method
    /// </summary>
    Task<List<ContentBoundary>> GetByComputationMethodAsync(
        string computationMethod,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get statistics across all boundaries
    /// </summary>
    Task<ContentBoundaryStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);
}
