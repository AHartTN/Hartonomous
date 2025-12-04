using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Represents a spatial boundary containing all atoms from a content ingestion
/// Uses convex hull (POLYGONZM) to define document "footprint" in 4D space
/// Enables document similarity queries via geometric overlap
/// </summary>
public sealed class ContentBoundary : BaseEntity
{
    /// <summary>Foreign key to content ingestion</summary>
    public Guid ContentIngestionId { get; private set; }
    
    /// <summary>Convex hull containing all atoms in content (POLYGONZM)</summary>
    public Polygon BoundaryGeometry { get; private set; } = null!;
    
    /// <summary>4D bounding box (axis-aligned minimum bounding rectangle)</summary>
    public BoundingBox4D BoundingBox { get; private set; } = null!;
    
    /// <summary>4D "area" of boundary (hypersurface measure)</summary>
    public double BoundaryArea { get; private set; }
    
    /// <summary>Perimeter length of boundary</summary>
    public double BoundaryPerimeter { get; private set; }
    
    /// <summary>Number of atoms within boundary</summary>
    public int AtomCount { get; private set; }
    
    /// <summary>Density: atoms per unit area</summary>
    public double Density { get; private set; }
    
    /// <summary>Centroid of boundary (geometric center)</summary>
    public Point Centroid { get; private set; } = null!;
    
    /// <summary>Timestamp of boundary computation</summary>
    public DateTime ComputedAt { get; private set; }
    
    /// <summary>Algorithm used to compute boundary</summary>
    public string ComputationMethod { get; private set; } = "ConvexHull";
    
    // Navigation property
    public ContentIngestion ContentIngestion { get; private set; } = null!;
    
    // Convenience properties (delegated to BoundingBox)
    public double MinX => BoundingBox.MinX;
    public double MaxX => BoundingBox.MaxX;
    public double MinY => BoundingBox.MinY;
    public double MaxY => BoundingBox.MaxY;
    public double MinZ => BoundingBox.MinZ;
    public double MaxZ => BoundingBox.MaxZ;
    
    private ContentBoundary() { } // EF Core constructor
    
    /// <summary>
    /// Create content boundary from atom locations using convex hull
    /// </summary>
    /// <param name="contentIngestionId">ID of the content ingestion</param>
    /// <param name="atoms">List of constants (atoms) to bound</param>
    /// <returns>New ContentBoundary instance</returns>
    public static ContentBoundary Create(
        Guid contentIngestionId,
        List<Constant> atoms)
    {
        if (contentIngestionId == Guid.Empty)
        {
            throw new ArgumentException("Content ingestion ID cannot be empty", nameof(contentIngestionId));
        }
        
        if (atoms == null || atoms.Count < 3)
        {
            throw new ArgumentException(
                "Need at least 3 atoms for convex hull computation", 
                nameof(atoms));
        }
        
        // Validate all atoms have locations
        var atomsWithLocations = atoms.Where(a => a.Location != null).ToList();
        if (atomsWithLocations.Count < 3)
        {
            throw new ArgumentException(
                "Need at least 3 atoms with valid locations", 
                nameof(atoms));
        }
        
        // Collect all atom locations
        var points = atomsWithLocations.Select(a => a.Location!).ToArray();
        var multiPoint = new MultiPoint(points) { SRID = 4326 };
        
        // Compute convex hull
        var convexHull = multiPoint.ConvexHull() as Polygon
            ?? throw new InvalidOperationException("Convex hull computation failed to produce a polygon");
        
        convexHull.SRID = 4326;
        
        // Compute centroid
        var centroid = convexHull.Centroid;
        centroid.SRID = 4326;
        
        // Compute 4D bounding box
        var boundingBox = BoundingBox4D.FromGeometry(convexHull);
        
        // Compute area and perimeter
        var area = convexHull.Area;
        var perimeter = convexHull.Length; // Length property gives perimeter for polygons
        
        // Compute density
        var density = area > 0 ? atomsWithLocations.Count / area : 0;
        
        var now = DateTime.UtcNow;
        
        return new ContentBoundary
        {
            Id = Guid.NewGuid(),
            ContentIngestionId = contentIngestionId,
            BoundaryGeometry = convexHull,
            BoundingBox = boundingBox,
            BoundaryArea = area,
            BoundaryPerimeter = perimeter,
            AtomCount = atomsWithLocations.Count,
            Density = density,
            Centroid = centroid,
            ComputedAt = now,
            ComputationMethod = "ConvexHull",
            CreatedAt = now,
            CreatedBy = "System"
        };
    }
    
    /// <summary>
    /// Create content boundary using custom polygon (e.g., from Voronoi cell)
    /// </summary>
    public static ContentBoundary CreateFromPolygon(
        Guid contentIngestionId,
        Polygon polygon,
        int atomCount,
        string computationMethod = "Custom")
    {
        if (contentIngestionId == Guid.Empty)
        {
            throw new ArgumentException("Content ingestion ID cannot be empty", nameof(contentIngestionId));
        }
        
        if (polygon == null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }
        
        if (atomCount < 0)
        {
            throw new ArgumentException("Atom count cannot be negative", nameof(atomCount));
        }
        
        polygon.SRID = 4326;
        
        var centroid = polygon.Centroid;
        centroid.SRID = 4326;
        
        var boundingBox = BoundingBox4D.FromGeometry(polygon);
        var area = polygon.Area;
        var perimeter = polygon.Length;
        var density = area > 0 ? atomCount / area : 0;
        
        var now = DateTime.UtcNow;
        
        return new ContentBoundary
        {
            Id = Guid.NewGuid(),
            ContentIngestionId = contentIngestionId,
            BoundaryGeometry = polygon,
            BoundingBox = boundingBox,
            BoundaryArea = area,
            BoundaryPerimeter = perimeter,
            AtomCount = atomCount,
            Density = density,
            Centroid = centroid,
            ComputedAt = now,
            ComputationMethod = computationMethod,
            CreatedAt = now,
            CreatedBy = "System"
        };
    }
    
    /// <summary>
    /// Check if a constant location is within this boundary
    /// </summary>
    public bool Contains(Constant constant)
    {
        if (constant?.Location == null)
        {
            return false;
        }
        
        return BoundaryGeometry.Contains(constant.Location);
    }
    
    /// <summary>
    /// Check if a point is within this boundary
    /// </summary>
    public bool Contains(Point point)
    {
        if (point == null)
        {
            return false;
        }
        
        return BoundaryGeometry.Contains(point);
    }
    
    /// <summary>
    /// Compute Jaccard similarity with another boundary (intersection over union)
    /// </summary>
    /// <param name="other">Target boundary for comparison</param>
    /// <returns>Jaccard similarity in range [0, 1]</returns>
    public double ComputeJaccardSimilarity(ContentBoundary other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        var intersection = BoundaryGeometry.Intersection(other.BoundaryGeometry);
        var union = BoundaryGeometry.Union(other.BoundaryGeometry);
        
        var unionArea = union.Area;
        if (unionArea == 0)
        {
            return 0;
        }
        
        return intersection.Area / unionArea;
    }
    
    /// <summary>
    /// Compute overlap coefficient with another boundary
    /// </summary>
    /// <returns>Overlap coefficient in range [0, 1]</returns>
    public double ComputeOverlapCoefficient(ContentBoundary other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        var intersection = BoundaryGeometry.Intersection(other.BoundaryGeometry);
        var minArea = Math.Min(BoundaryArea, other.BoundaryArea);
        
        if (minArea == 0)
        {
            return 0;
        }
        
        return intersection.Area / minArea;
    }
    
    /// <summary>
    /// Compute Dice coefficient with another boundary
    /// </summary>
    /// <returns>Dice coefficient in range [0, 1]</returns>
    public double ComputeDiceCoefficient(ContentBoundary other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        var intersection = BoundaryGeometry.Intersection(other.BoundaryGeometry);
        var sumArea = BoundaryArea + other.BoundaryArea;
        
        if (sumArea == 0)
        {
            return 0;
        }
        
        return (2.0 * intersection.Area) / sumArea;
    }
    
    /// <summary>
    /// Check if this boundary intersects with another
    /// </summary>
    public bool Intersects(ContentBoundary other)
    {
        if (other == null)
        {
            return false;
        }
        
        return BoundaryGeometry.Intersects(other.BoundaryGeometry);
    }
    
    /// <summary>
    /// Compute distance from this boundary's centroid to another
    /// </summary>
    public double DistanceTo(ContentBoundary other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        return Centroid.Distance(other.Centroid);
    }
    
    /// <summary>
    /// Check if this boundary completely contains another
    /// </summary>
    public bool Contains(ContentBoundary other)
    {
        if (other == null)
        {
            return false;
        }
        
        return BoundaryGeometry.Contains(other.BoundaryGeometry);
    }
    
    /// <summary>
    /// Recompute boundary statistics (useful after geometry updates)
    /// </summary>
    public void RecalculateStatistics()
    {
        BoundaryArea = BoundaryGeometry.Area;
        BoundaryPerimeter = BoundaryGeometry.Length;
        Density = BoundaryArea > 0 ? AtomCount / BoundaryArea : 0;
        
        Centroid = BoundaryGeometry.Centroid;
        Centroid.SRID = 4326;
        
        BoundingBox = BoundingBox4D.FromGeometry(BoundaryGeometry);
        
        UpdatedAt = DateTime.UtcNow;
    }
}
