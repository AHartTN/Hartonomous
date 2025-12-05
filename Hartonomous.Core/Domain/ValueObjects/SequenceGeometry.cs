using Hartonomous.Core.Domain.Common;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.ValueObjects;

/// <summary>
/// Represents a sequence of constants as a LINESTRINGZM geometry
/// Used for BPE token compositions and sequential patterns
/// X = Hilbert index, Y = entropy, Z = compressibility, M = connectivity
/// </summary>
public sealed class SequenceGeometry : ValueObject
{
    /// <summary>Ordered list of constant IDs in sequence</summary>
    public IReadOnlyList<Guid> ConstantIds { get; private init; }
    
    /// <summary>LINESTRINGZM geometry representing the sequence in 4D space</summary>
    public LineString Geometry { get; private init; }
    
    /// <summary>Total length of sequence in Hilbert space</summary>
    public ulong HilbertLength { get; private init; }
    
    /// <summary>Number of constants in sequence</summary>
    public int Length => ConstantIds.Count;
    
    private SequenceGeometry(IReadOnlyList<Guid> constantIds, LineString geometry, ulong hilbertLength)
    {
        ConstantIds = constantIds;
        Geometry = geometry;
        HilbertLength = hilbertLength;
    }
    
    /// <summary>
    /// Create sequence geometry from ordered spatial coordinates
    /// </summary>
    public static SequenceGeometry FromCoordinates(
        IReadOnlyList<Guid> constantIds,
        IReadOnlyList<SpatialCoordinate> coordinates)
    {
        if (constantIds == null || constantIds.Count == 0)
            throw new ArgumentException("Constant IDs cannot be null or empty", nameof(constantIds));
        
        if (coordinates == null || coordinates.Count == 0)
            throw new ArgumentException("Coordinates cannot be null or empty", nameof(coordinates));
        
        if (constantIds.Count != coordinates.Count)
            throw new ArgumentException("Constant IDs and coordinates must have same count");
        
        // Build LINESTRINGZM from coordinates
        var coords = coordinates
            .Select(c => new CoordinateZM(c.X, c.Y, c.Z, c.M))
            .ToArray();
        
        var lineString = new LineString(coords) { SRID = 0 };
        
        // Compute total Hilbert length using actual Hilbert indices
        ulong hilbertLength = 0;
        for (int i = 1; i < coordinates.Count; i++)
        {
            var dist = Math.Abs((long)coordinates[i].HilbertHigh - (long)coordinates[i-1].HilbertHigh);
            if (dist == 0 && coordinates[i].HilbertLow != coordinates[i-1].HilbertLow)
            {
                // If HilbertHigh is same, check HilbertLow
                dist = Math.Abs((long)coordinates[i].HilbertLow - (long)coordinates[i-1].HilbertLow);
            }
            hilbertLength += (ulong)dist;
        }
        
        return new SequenceGeometry(constantIds, lineString, hilbertLength);
    }
    
    /// <summary>
    /// Detect gaps in Hilbert sequence (compression opportunities)
    /// </summary>
    public IEnumerable<(int StartIndex, int EndIndex, ulong GapSize)> DetectGaps(
        ulong gapThreshold = 1000)
    {
        var gaps = new List<(int, int, ulong)>();
        
        for (int i = 0; i < Geometry.Coordinates.Length - 1; i++)
        {
            var currentX = Geometry.Coordinates[i].X;
            var nextX = Geometry.Coordinates[i + 1].X;
            var gap = (ulong)Math.Abs(nextX - currentX);
            
            if (gap > gapThreshold)
            {
                gaps.Add((i, i + 1, gap));
            }
        }
        
        return gaps;
    }
    
    /// <summary>
    /// Get centroid of sequence in 4D space
    /// </summary>
    public SpatialCoordinate GetCentroid()
    {
        if (Geometry.Coordinates.Length == 0)
            throw new InvalidOperationException("Cannot compute centroid of empty sequence");
        
        var avgX = Geometry.Coordinates.Average(c => c.X);
        var avgY = Geometry.Coordinates.Average(c => c.Y);
        var avgZ = Geometry.Coordinates.Average(c => c.Z);
        var avgM = Geometry.Coordinates.Average(c => c.M);
        
        return SpatialCoordinate.FromUniversalProperties(
            (uint)avgX,
            (int)avgY,
            (int)avgZ,
            (int)avgM);
    }
    
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Geometry.EqualsExact(Geometry);
        foreach (var id in ConstantIds)
            yield return id;
    }
}
