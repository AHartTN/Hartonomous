using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Repository interface for Landmark entity with spatial operations
/// </summary>
public interface ILandmarkRepository : IRepository<Landmark>
{
    /// <summary>
    /// Get landmark by name
    /// </summary>
    Task<Landmark?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find landmarks that contain a specific coordinate
    /// </summary>
    Task<IEnumerable<Landmark>> GetContainingLandmarksAsync(
        SpatialCoordinate coordinate,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find landmarks near a coordinate
    /// </summary>
    Task<IEnumerable<Landmark>> GetNearbyLandmarksAsync(
        SpatialCoordinate center,
        double maxDistance,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all active landmarks
    /// </summary>
    Task<IEnumerable<Landmark>> GetActiveLandmarksAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get landmarks ordered by density (high to low)
    /// </summary>
    Task<IEnumerable<Landmark>> GetByDensityAsync(
        int count = 100,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get landmarks with constant count in range
    /// </summary>
    Task<IEnumerable<Landmark>> GetByConstantCountRangeAsync(
        long minCount,
        long maxCount,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find nearest landmark to a coordinate
    /// </summary>
    Task<Landmark?> GetNearestLandmarkAsync(
        SpatialCoordinate coordinate,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if landmark name exists
    /// </summary>
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
}
