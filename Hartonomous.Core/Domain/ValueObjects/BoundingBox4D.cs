using Hartonomous.Core.Domain.Common;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.ValueObjects;

/// <summary>
/// Represents a 4D axis-aligned bounding box (AABB) in XYZM space
/// Used for spatial indexing and query optimization in 4D geometric space
/// </summary>
public sealed class BoundingBox4D : ValueObject
{
    /// <summary>Minimum X coordinate</summary>
    public double MinX { get; private init; }
    
    /// <summary>Maximum X coordinate</summary>
    public double MaxX { get; private init; }
    
    /// <summary>Minimum Y coordinate</summary>
    public double MinY { get; private init; }
    
    /// <summary>Maximum Y coordinate</summary>
    public double MaxY { get; private init; }
    
    /// <summary>Minimum Z coordinate</summary>
    public double MinZ { get; private init; }
    
    /// <summary>Maximum Z coordinate</summary>
    public double MaxZ { get; private init; }
    
    /// <summary>Minimum M coordinate (optional 4th dimension)</summary>
    public double MinM { get; private init; }
    
    /// <summary>Maximum M coordinate (optional 4th dimension)</summary>
    public double MaxM { get; private init; }
    
    /// <summary>Width (range in X dimension)</summary>
    public double Width => MaxX - MinX;
    
    /// <summary>Height (range in Y dimension)</summary>
    public double Height => MaxY - MinY;
    
    /// <summary>Depth (range in Z dimension)</summary>
    public double Depth => MaxZ - MinZ;
    
    /// <summary>Measure range (range in M dimension)</summary>
    public double MeasureRange => MaxM - MinM;
    
    /// <summary>4D volume (hypervolume)</summary>
    public double HyperVolume => Width * Height * Depth * MeasureRange;
    
    /// <summary>Center point of bounding box</summary>
    public (double X, double Y, double Z, double M) Center => (
        (MinX + MaxX) / 2.0,
        (MinY + MaxY) / 2.0,
        (MinZ + MaxZ) / 2.0,
        (MinM + MaxM) / 2.0
    );
    
    /// <summary>Whether this is a degenerate (zero-volume) bounding box</summary>
    public bool IsDegenerate => Width == 0 || Height == 0 || Depth == 0 || MeasureRange == 0;
    
    /// <summary>Whether this is a point (all dimensions have zero range)</summary>
    public bool IsPoint => Width == 0 && Height == 0 && Depth == 0 && MeasureRange == 0;
    
    private BoundingBox4D() { } // For EF Core
    
    /// <summary>
    /// Create bounding box from min/max coordinates
    /// </summary>
    public static BoundingBox4D FromBounds(
        double minX, double maxX,
        double minY, double maxY,
        double minZ, double maxZ,
        double minM = 0, double maxM = 0)
    {
        if (minX > maxX)
            throw new ArgumentException($"MinX ({minX}) cannot be greater than MaxX ({maxX})");
        
        if (minY > maxY)
            throw new ArgumentException($"MinY ({minY}) cannot be greater than MaxY ({maxY})");
        
        if (minZ > maxZ)
            throw new ArgumentException($"MinZ ({minZ}) cannot be greater than MaxZ ({maxZ})");
        
        if (minM > maxM)
            throw new ArgumentException($"MinM ({minM}) cannot be greater than MaxM ({maxM})");
        
        return new BoundingBox4D
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            MinZ = minZ,
            MaxZ = maxZ,
            MinM = minM,
            MaxM = maxM
        };
    }
    
    /// <summary>
    /// Create bounding box from a geometry (analyzes all coordinates)
    /// </summary>
    public static BoundingBox4D FromGeometry(Geometry geometry)
    {
        if (geometry == null)
            throw new ArgumentNullException(nameof(geometry));
        
        if (!geometry.Coordinates.Any())
            throw new ArgumentException("Geometry has no coordinates", nameof(geometry));
        
        double minX = double.MaxValue;
        double maxX = double.MinValue;
        double minY = double.MaxValue;
        double maxY = double.MinValue;
        double minZ = double.MaxValue;
        double maxZ = double.MinValue;
        double minM = double.MaxValue;
        double maxM = double.MinValue;
        
        foreach (var coord in geometry.Coordinates)
        {
            // X and Y are always present
            minX = Math.Min(minX, coord.X);
            maxX = Math.Max(maxX, coord.X);
            minY = Math.Min(minY, coord.Y);
            maxY = Math.Max(maxY, coord.Y);
            
            // Z coordinate (handle NaN for 2D coordinates)
            if (!double.IsNaN(coord.Z))
            {
                minZ = Math.Min(minZ, coord.Z);
                maxZ = Math.Max(maxZ, coord.Z);
            }
            
            // M coordinate (handle NaN for non-measured coordinates)
            if (!double.IsNaN(coord.M))
            {
                minM = Math.Min(minM, coord.M);
                maxM = Math.Max(maxM, coord.M);
            }
        }
        
        // Default Z to 0 if no valid Z coordinates found
        if (minZ == double.MaxValue) minZ = 0;
        if (maxZ == double.MinValue) maxZ = 0;
        
        // Default M to 0 if no valid M coordinates found
        if (minM == double.MaxValue) minM = 0;
        if (maxM == double.MinValue) maxM = 0;
        
        return new BoundingBox4D
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            MinZ = minZ,
            MaxZ = maxZ,
            MinM = minM,
            MaxM = maxM
        };
    }
    
    /// <summary>
    /// Create bounding box from collection of geometries (union of all bounds)
    /// </summary>
    public static BoundingBox4D FromGeometries(IEnumerable<Geometry> geometries)
    {
        if (geometries == null)
            throw new ArgumentNullException(nameof(geometries));
        
        var geomList = geometries.ToList();
        if (geomList.Count == 0)
            throw new ArgumentException("Geometry collection is empty", nameof(geometries));
        
        var boundingBoxes = geomList.Select(FromGeometry).ToList();
        
        return new BoundingBox4D
        {
            MinX = boundingBoxes.Min(b => b.MinX),
            MaxX = boundingBoxes.Max(b => b.MaxX),
            MinY = boundingBoxes.Min(b => b.MinY),
            MaxY = boundingBoxes.Max(b => b.MaxY),
            MinZ = boundingBoxes.Min(b => b.MinZ),
            MaxZ = boundingBoxes.Max(b => b.MaxZ),
            MinM = boundingBoxes.Min(b => b.MinM),
            MaxM = boundingBoxes.Max(b => b.MaxM)
        };
    }
    
    /// <summary>
    /// Create bounding box from a single point
    /// </summary>
    public static BoundingBox4D FromPoint(Point point)
    {
        if (point == null)
            throw new ArgumentNullException(nameof(point));
        
        double z = double.IsNaN(point.Z) ? 0 : point.Z;
        double m = double.IsNaN(point.M) ? 0 : point.M;
        
        return new BoundingBox4D
        {
            MinX = point.X,
            MaxX = point.X,
            MinY = point.Y,
            MaxY = point.Y,
            MinZ = z,
            MaxZ = z,
            MinM = m,
            MaxM = m
        };
    }
    
    /// <summary>
    /// Check if this bounding box contains a point
    /// </summary>
    public bool Contains(double x, double y, double z, double m)
    {
        return x >= MinX && x <= MaxX &&
               y >= MinY && y <= MaxY &&
               z >= MinZ && z <= MaxZ &&
               m >= MinM && m <= MaxM;
    }
    
    /// <summary>
    /// Check if this bounding box contains a point geometry
    /// </summary>
    public bool Contains(Point point)
    {
        if (point == null) return false;
        
        double z = double.IsNaN(point.Z) ? 0 : point.Z;
        double m = double.IsNaN(point.M) ? 0 : point.M;
        
        return Contains(point.X, point.Y, z, m);
    }
    
    /// <summary>
    /// Check if this bounding box completely contains another bounding box
    /// </summary>
    public bool Contains(BoundingBox4D other)
    {
        if (other == null) return false;
        
        return other.MinX >= MinX && other.MaxX <= MaxX &&
               other.MinY >= MinY && other.MaxY <= MaxY &&
               other.MinZ >= MinZ && other.MaxZ <= MaxZ &&
               other.MinM >= MinM && other.MaxM <= MaxM;
    }
    
    /// <summary>
    /// Check if this bounding box intersects with another
    /// </summary>
    public bool Intersects(BoundingBox4D other)
    {
        if (other == null) return false;
        
        return !(other.MaxX < MinX || other.MinX > MaxX ||
                 other.MaxY < MinY || other.MinY > MaxY ||
                 other.MaxZ < MinZ || other.MinZ > MaxZ ||
                 other.MaxM < MinM || other.MinM > MaxM);
    }
    
    /// <summary>
    /// Compute intersection with another bounding box
    /// Returns null if bounding boxes don't intersect
    /// </summary>
    public BoundingBox4D? Intersection(BoundingBox4D other)
    {
        if (other == null || !Intersects(other))
            return null;
        
        return new BoundingBox4D
        {
            MinX = Math.Max(MinX, other.MinX),
            MaxX = Math.Min(MaxX, other.MaxX),
            MinY = Math.Max(MinY, other.MinY),
            MaxY = Math.Min(MaxY, other.MaxY),
            MinZ = Math.Max(MinZ, other.MinZ),
            MaxZ = Math.Min(MaxZ, other.MaxZ),
            MinM = Math.Max(MinM, other.MinM),
            MaxM = Math.Min(MaxM, other.MaxM)
        };
    }
    
    /// <summary>
    /// Compute union (smallest bounding box containing both)
    /// </summary>
    public BoundingBox4D Union(BoundingBox4D other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        
        return new BoundingBox4D
        {
            MinX = Math.Min(MinX, other.MinX),
            MaxX = Math.Max(MaxX, other.MaxX),
            MinY = Math.Min(MinY, other.MinY),
            MaxY = Math.Max(MaxY, other.MaxY),
            MinZ = Math.Min(MinZ, other.MinZ),
            MaxZ = Math.Max(MaxZ, other.MaxZ),
            MinM = Math.Min(MinM, other.MinM),
            MaxM = Math.Max(MaxM, other.MaxM)
        };
    }
    
    /// <summary>
    /// Expand bounding box by delta in all directions
    /// </summary>
    public BoundingBox4D Expand(double delta)
    {
        return new BoundingBox4D
        {
            MinX = MinX - delta,
            MaxX = MaxX + delta,
            MinY = MinY - delta,
            MaxY = MaxY + delta,
            MinZ = MinZ - delta,
            MaxZ = MaxZ + delta,
            MinM = MinM - delta,
            MaxM = MaxM + delta
        };
    }
    
    /// <summary>
    /// Expand bounding box by different deltas per dimension
    /// </summary>
    public BoundingBox4D Expand(double deltaX, double deltaY, double deltaZ, double deltaM)
    {
        return new BoundingBox4D
        {
            MinX = MinX - deltaX,
            MaxX = MaxX + deltaX,
            MinY = MinY - deltaY,
            MaxY = MaxY + deltaY,
            MinZ = MinZ - deltaZ,
            MaxZ = MaxZ + deltaZ,
            MinM = MinM - deltaM,
            MaxM = MaxM + deltaM
        };
    }
    
    /// <summary>
    /// Convert to PostGIS Box3D string representation (for queries)
    /// </summary>
    public string ToBox3DString()
    {
        return $"BOX3D({MinX} {MinY} {MinZ}, {MaxX} {MaxY} {MaxZ})";
    }
    
    /// <summary>
    /// Convert to WKT BOX string (2D only)
    /// </summary>
    public string ToWktBox()
    {
        return $"BOX({MinX} {MinY}, {MaxX} {MaxY})";
    }
    
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MinX;
        yield return MaxX;
        yield return MinY;
        yield return MaxY;
        yield return MinZ;
        yield return MaxZ;
        yield return MinM;
        yield return MaxM;
    }
    
    public override string ToString()
    {
        return $"BoundingBox4D[X:({MinX:F2},{MaxX:F2}) Y:({MinY:F2},{MaxY:F2}) Z:({MinZ:F2},{MaxZ:F2}) M:({MinM:F2},{MaxM:F2})]";
    }
}
