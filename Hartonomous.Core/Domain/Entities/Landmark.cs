using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Spatial landmark representing a region in 3D coordinate space
/// Used for organizing constants and enabling efficient spatial queries
/// </summary>
public class Landmark : BaseEntity
{
    /// <summary>Unique identifier for the landmark</summary>
    public string Name { get; private set; } = null!;
    
    /// <summary>Description of the landmark</summary>
    public string? Description { get; private set; }
    
    /// <summary>Center coordinate of the landmark region</summary>
    public SpatialCoordinate Center { get; private set; } = null!;
    
    /// <summary>PostGIS Point for spatial indexing</summary>
    public Point Location { get; private set; } = null!;
    
    /// <summary>Radius defining the landmark's influence region</summary>
    public double Radius { get; private set; }
    
    /// <summary>Number of constants mapped to this landmark</summary>
    public long ConstantCount { get; private set; }
    
    /// <summary>Average distance of constants from landmark center</summary>
    public double AverageDistance { get; private set; }
    
    /// <summary>Timestamp of last statistics update</summary>
    public DateTime LastStatisticsUpdate { get; private set; }
    
    /// <summary>Whether this landmark is actively maintained</summary>
    public bool IsActive { get; private set; }
    
    /// <summary>Density metric: constants per unit volume</summary>
    public double Density { get; private set; }
    
    // Navigation properties
    public ICollection<Constant> Constants { get; private set; } = new List<Constant>();
    
    private Landmark() { } // EF Core constructor
    
    public static Landmark Create(string name, SpatialCoordinate center, double radius, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Landmark name cannot be null or empty", nameof(name));
        }
        
        if (center == null)
        {
            throw new ArgumentNullException(nameof(center));
        }
        
        if (radius <= 0 || radius > 2.0)
        {
            throw new ArgumentException("Radius must be positive and <= 2.0 (max distance in normalized space)", nameof(radius));
        }
        
        var now = DateTime.UtcNow;
        
        var landmark = new Landmark
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Center = center,
            Location = new Point(center.X, center.Y, center.Z) { SRID = 0 },
            Radius = radius,
            ConstantCount = 0,
            AverageDistance = 0.0,
            LastStatisticsUpdate = now,
            IsActive = true,
            Density = 0.0,
            CreatedAt = now
        };
        
        return landmark;
    }
    
    public void UpdateStatistics(long constantCount, double averageDistance)
    {
        if (constantCount < 0)
        {
            throw new ArgumentException("Constant count cannot be negative", nameof(constantCount));
        }
        
        if (averageDistance < 0)
        {
            throw new ArgumentException("Average distance cannot be negative", nameof(averageDistance));
        }
        
        ConstantCount = constantCount;
        AverageDistance = averageDistance;
        
        // Calculate density: constants per unit volume
        // Volume of sphere: (4/3) * π * r³
        var volume = (4.0 / 3.0) * Math.PI * Math.Pow(Radius, 3);
        Density = volume > 0 ? constantCount / volume : 0.0;
        
        LastStatisticsUpdate = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void UpdateRadius(double newRadius)
    {
        if (newRadius <= 0 || newRadius > 2.0)
        {
            throw new ArgumentException("Radius must be positive and <= 2.0", nameof(newRadius));
        }
        
        Radius = newRadius;
        UpdatedAt = DateTime.UtcNow;
        
        // Recalculate density with new radius
        if (ConstantCount > 0)
        {
            var volume = (4.0 / 3.0) * Math.PI * Math.Pow(Radius, 3);
            Density = volume > 0 ? ConstantCount / volume : 0.0;
        }
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
    
    public bool ContainsPoint(SpatialCoordinate coordinate)
    {
        if (coordinate == null)
        {
            throw new ArgumentNullException(nameof(coordinate));
        }
        
        var distance = Center.DistanceTo(coordinate);
        return distance <= Radius;
    }
}
