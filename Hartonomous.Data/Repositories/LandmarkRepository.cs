using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;
using Hartonomous.Data.Extensions; // Assuming this contains WhereHilbertRange and OrderByHilbertDistance if still needed
using Hartonomous.Marshal;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data.Repositories;

/// <summary>
/// Repository implementation for Landmark entity, updated for Deterministic Hilbert Landmarks.
/// </summary>
public class LandmarkRepository : Repository<Landmark>, ILandmarkRepository
{
    public LandmarkRepository(ApplicationDbContext context) : base(context)
    {
    }
    
    public async Task<Landmark?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be null or empty", nameof(name));
        }
        
        return await _dbSet
            .FirstOrDefaultAsync(l => l.Name == name, cancellationToken);
    }
    
    /// <summary>
    /// Finds landmarks that contain the specified spatial coordinate.
    /// Since landmarks are now Hilbert tiles, this means finding the tile that *is* this coordinate's tile.
    /// </summary>
    /// <param name="coordinate">The spatial coordinate to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of landmarks that contain the coordinate (typically one per level).</returns>
    public async Task<IEnumerable<Landmark>> GetContainingLandmarksAsync(
        SpatialCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        if (coordinate == null)
        {
            throw new ArgumentNullException(nameof(coordinate));
        }
        
        // We find the Hilbert Tile ID for the given coordinate at all defined landmark levels.
        // For simplicity, let's query for a few representative levels (e.g., matching common DetectionLevels).
        // Or, if we expect only one landmark at a specific level, we could take 'coordinate.Precision'.
        // Let's assume a fixed level for now (e.g., Level 15 as a representative granularity).
        int queryLevel = 15; // This could be configurable or determined dynamically

        var (tileHigh, tileLow) = HilbertCurve4D.GetHilbertTileId(
            coordinate.HilbertHigh, coordinate.HilbertLow, queryLevel, coordinate.Precision);

        // Query for landmarks that exactly match this tile ID and level
        return await _dbSet
            .Where(l => l.IsActive && 
                        l.HilbertPrefixHigh == tileHigh && 
                        l.HilbertPrefixLow == tileLow &&
                        l.Level == queryLevel)
            .ToListAsync(cancellationToken);
    }
    
    /// <summary>
    /// Finds landmarks nearby a given spatial coordinate within a maximum distance.
    /// This uses a two-phase approach: Hilbert range query (DB) then exact 4D distance (in-memory).
    /// </summary>
    /// <param name="center">The center coordinate for the proximity search.</param>
    /// <param name="maxDistance">The maximum 4D Euclidean distance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of nearby landmarks, ordered by distance.</returns>
    public async Task<IEnumerable<Landmark>> GetNearbyLandmarksAsync(
        SpatialCoordinate center,
        double maxDistance,
        CancellationToken cancellationToken = default)
    {
        if (center == null)
        {
            throw new ArgumentNullException(nameof(center));
        }
        
        if (maxDistance <= 0)
        {
            throw new ArgumentException("Max distance must be positive", nameof(maxDistance));
        }
        
        // Calculate the Hilbert range for the given center and maxDistance.
        // This gives us a bounding box in Hilbert space.
        var (minHigh, minLow, maxHigh, maxLow) = center.GetHilbertRangeForRadius(maxDistance);
        
        // Query for landmarks whose Hilbert prefixes overlap with this range.
        // This leverages the B-tree index on Hilbert prefixes.
        // Since landmarks are tiles, we check if their prefix falls within the query range.
        var candidateLandmarks = await _dbSet
            .Where(l => l.IsActive &&
                        ((l.HilbertPrefixHigh > minHigh) || (l.HilbertPrefixHigh == minHigh && l.HilbertPrefixLow >= minLow)) &&
                        ((l.HilbertPrefixHigh < maxHigh) || (l.HilbertPrefixHigh == maxHigh && l.HilbertPrefixLow <= maxLow)))
            .ToListAsync(cancellationToken); // Fetch all candidates that broadly match the Hilbert range
            
        // Now, filter and order in-memory using the actual 4D Euclidean distance to the landmark's computed center.
        // This uses the computed 'Center' property of the Landmark entity.
        return candidateLandmarks
            .Where(l => l.Center.DistanceTo(center) <= maxDistance)
            .OrderBy(l => l.Center.DistanceTo(center))
            .ToList();
    }
    
    /// <summary>
    /// Finds the single nearest landmark to a given spatial coordinate.
    /// </summary>
    /// <param name="coordinate">The spatial coordinate to find the nearest landmark for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The nearest landmark, or null if none are active.</returns>
    public async Task<Landmark?> GetNearestLandmarkAsync(
        SpatialCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        if (coordinate == null)
        {
            throw new ArgumentNullException(nameof(coordinate));
        }
        
        // Fetch all active landmarks (in a real system, this would be optimized to fetch candidates efficiently,
        // e.g., by first querying a coarse Hilbert range).
        var activeLandmarks = await _dbSet
            .Where(l => l.IsActive)
            .ToListAsync(cancellationToken);

        // Order by distance to the landmark's computed center and take the first one.
        return activeLandmarks
            .OrderBy(l => l.Center.DistanceTo(coordinate))
            .FirstOrDefault();
    }
    
    public async Task<IEnumerable<Landmark>> GetActiveLandmarksAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<Landmark>> GetByDensityAsync(
        int count = 100,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            throw new ArgumentException("Count must be positive", nameof(count));
        }
        
        return await _dbSet
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.Density)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IEnumerable<Landmark>> GetByConstantCountRangeAsync(
        long minCount,
        long maxCount,
        CancellationToken cancellationToken = default)
    {
        if (minCount < 0 || maxCount < minCount)
        {
            throw new ArgumentException("Invalid count range");
        }
        
        return await _dbSet
            .Where(l => l.ConstantCount >= minCount && l.ConstantCount <= maxCount)
            .OrderByDescending(l => l.ConstantCount)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be null or empty", nameof(name));
        }
        
        return await _dbSet.AnyAsync(l => l.Name == name, cancellationToken);
    }
}