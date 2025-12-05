using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.Utilities;
using Hartonomous.Core.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Deterministic Hilbert Landmark representing a specific region (tile) in 4D Hilbert space.
/// Used for organizing constants and enabling efficient spatial queries.
/// Replaces heuristic clustering with a fixed, hierarchical grid.
/// </summary>
public class Landmark : BaseEntity
{
    /// <summary>Unique identifier for the landmark, derived from HilbertPrefix and Level</summary>
    public string Name { get; private set; } = null!;
    
    /// <summary>Description of the landmark</summary>
    public string? Description { get; private set; }
    
    /// <summary>Upper 42 bits of the Hilbert tile ID (prefix)</summary>
    public ulong HilbertPrefixHigh { get; private set; }
    
    /// <summary>Lower 42 bits of the Hilbert tile ID (prefix)</summary>
    public ulong HilbertPrefixLow { get; private set; }
    
    /// <summary>The precision level (depth) of this Hilbert tile</summary>
    public int Level { get; private set; }
    
    /// <summary>Number of constants mapped to this landmark</summary>
    public long ConstantCount { get; private set; }
    
    /// <summary>Density metric: constants per unit volume of the tile</summary>
    public double Density { get; private set; }
    
    /// <summary>Timestamp of last statistics update</summary>
    public DateTime LastStatisticsUpdate { get; private set; }
    
    /// <summary>Whether this landmark is actively maintained</summary>
    public bool IsActive { get; private set; }
    
    // Navigation properties
    public ICollection<Constant> Constants { get; private set; } = new List<Constant>();
    
    private Landmark() { } // EF Core constructor
    
    /// <summary>
    /// Creates a new deterministic Hilbert Landmark.
    /// </summary>
    /// <param name="hilbertPrefixHigh">Upper 42 bits of the Hilbert tile ID.</param>
    /// <param name="hilbertPrefixLow">Lower 42 bits of the Hilbert tile ID.</param>
    /// <param name="level">The precision level (depth) of the Hilbert tile (e.g., 10, 15, 20 bits).</param>
    /// <param name="description">Optional description for the landmark.</param>
    /// <returns>A new Landmark entity.</returns>
    public static Landmark Create(ulong hilbertPrefixHigh, ulong hilbertPrefixLow, int level, string? description = null)
    {
        if (level < 1 || level > HilbertCurve4D.MaxPrecision)
        {
            throw new ArgumentException($"Level must be between 1 and {HilbertCurve4D.MaxPrecision}", nameof(level));
        }
        
        var now = DateTime.UtcNow;
        var name = $"H:{hilbertPrefixHigh:X}-{hilbertPrefixLow:X}_L{level}"; // Unique, descriptive name
        
        var landmark = new Landmark
        {
            Id = Guid.NewGuid(), // Still need a Guid ID for DB primary key
            Name = name,
            Description = description,
            HilbertPrefixHigh = hilbertPrefixHigh,
            HilbertPrefixLow = hilbertPrefixLow,
            Level = level,
            ConstantCount = 0,
            Density = 0.0,
            LastStatisticsUpdate = now,
            IsActive = true,
            CreatedAt = now
        };
        
        return landmark;
    }
    
    /// <summary>
    /// Updates the statistics for this landmark (e.g., constant count, density).
    /// </summary>
    /// <param name="constantCount">The total number of constants now mapped to this landmark.</param>
    public void UpdateStatistics(long constantCount)
    {
        if (constantCount < 0)
        {
            throw new ArgumentException("Constant count cannot be negative", nameof(constantCount));
        }
        
        ConstantCount = constantCount;
        
        // Calculate density: constants per unit volume (hypervolume of the tile)
        // For a Hilbert tile, its "volume" is relative to the total space (2^84)
        // A tile at 'level' has 4 * level bits. So 2^(4 * (HilbertCurve4D.DefaultPrecision - level)) sub-tiles
        // The total number of possible tiles at this level is (2^(level * Dimensions)).
        // So the "volume" of a tile can be considered 1 / (2^(level * Dimensions)) of the total space.
        // Or, more simply, count / (area_at_this_level).
        // For now, let's use ConstantCount directly or assume unit volume for simplicity,
        // or calculate volume based on the level.
        // A level 'l' tile represents (2^(MaxPrecision - l)) ^ Dimensions coordinate space.
        // So, relative "volume" can be calculated as: (1.0 / Math.Pow(2, (double)(HilbertCurve4D.DefaultPrecision - Level) * HilbertCurve4D.Dimensions));
        
        // Let's approximate volume as 1 for a level. Density becomes count.
        // Or, better, the 'area' of the tile.
        double tileArea = Math.Pow(2, (double)Level * HilbertCurve4D.Dimensions);
        Density = constantCount / tileArea; // Relative density compared to a fully packed tile
        
        LastStatisticsUpdate = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Checks if a given Hilbert index falls within this landmark's tile.
    /// </summary>
    /// <param name="hilbertIndex">The full 84-bit Hilbert index of a constant.</param>
    /// <param name="originalPrecision">The original precision used to encode the full Hilbert index.</param>
    /// <returns>True if the constant is within this landmark's tile, false otherwise.</returns>
    public bool ContainsHilbertIndex((ulong High, ulong Low) hilbertIndex, int originalPrecision = HilbertCurve4D.DefaultPrecision)
    {
        var tileId = HilbertCurve4D.GetHilbertTileId(hilbertIndex.High, hilbertIndex.Low, Level, originalPrecision);
        return tileId.High == HilbertPrefixHigh && tileId.Low == HilbertPrefixLow;
    }
    
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void Reactivate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }
}