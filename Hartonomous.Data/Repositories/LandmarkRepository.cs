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
    private readonly ApplicationDbContext _dbContext;

    public LandmarkRepository(ApplicationDbContext context) : base(context)
    {
        _dbContext = context;
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
    /// Uses PostgreSQL function for database-side filtering with Hilbert distance approximation.
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

        // Call PostgreSQL function - returns landmarks within Hilbert distance approximation
        // Distance column is extra and will be ignored by EF Core during materialization
        var results = await _dbSet
            .FromSqlRaw(@"
                SELECT 
                    id, name, description, hilbert_prefix_high, hilbert_prefix_low,
                    level, constant_count, density, is_active, last_statistics_update,
                    created_at, created_by, updated_at, updated_by,
                    is_deleted, deleted_at, deleted_by
                FROM get_nearby_landmarks({0}, {1}, {2}, {3}, {4})
            ", 
            (long)center.HilbertHigh, 
            (long)center.HilbertLow, 
            maxDistance,
            center.Precision,
            100) // max_results
            .ToListAsync(cancellationToken);

        return results;
    }
    
    /// <summary>
    /// Finds the single nearest landmark to a given spatial coordinate.
    /// Uses PostgreSQL function for database-side query.
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

        // Call PostgreSQL function - returns single nearest landmark
        var results = await _dbSet
            .FromSqlRaw(@"
                SELECT * FROM get_nearest_landmark({0}, {1})
            ", 
            (long)coordinate.HilbertHigh, 
            (long)coordinate.HilbertLow)
            .ToListAsync(cancellationToken);

        return results.FirstOrDefault();
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