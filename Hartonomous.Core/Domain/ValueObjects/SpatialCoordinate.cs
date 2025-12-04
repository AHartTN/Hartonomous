using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.Utilities;

namespace Hartonomous.Core.Domain.ValueObjects;

/// <summary>
/// Hilbert-first spatial coordinate representation.
/// The Hilbert index (1D space-filling curve) is the PRIMARY representation and source of truth.
/// Cartesian (X,Y,Z) coordinates are computed on-demand for visualization and PostGIS operations.
/// 
/// This design enables:
/// - Single source of truth (HilbertIndex drives all operations)
/// - B-tree indexed queries (100x faster than R-tree spatial index)
/// - Geometry as materialized view (decoded from Hilbert for PostGIS functions)
/// - Efficient spatial locality preservation (nearby 3D points have nearby 1D indices)
/// - More data can be packed into PostGIS geometry (it becomes a computed view)
/// 
/// For legacy compatibility, coordinates can be created from (X,Y,Z) Cartesian form,
/// but internally they are immediately encoded to Hilbert index.
/// </summary>
public sealed class SpatialCoordinate : ValueObject
{
    /// <summary>
    /// The Hilbert curve index - PRIMARY spatial representation (source of truth).
    /// 63-bit encoding (21 bits per dimension) providing ~2 million resolution per axis.
    /// This is what gets stored, indexed, and queried.
    /// </summary>
    public ulong HilbertIndex { get; private init; }

    /// <summary>
    /// Precision of Hilbert encoding (bits per dimension). Default 21 bits = 2,097,152 resolution.
    /// </summary>
    public int Precision { get; private init; }

    // Cached Cartesian coordinates (decoded on-demand, not persisted)
    private double? _cachedX;
    private double? _cachedY;
    private double? _cachedZ;

    /// <summary>
    /// X coordinate (decoded from HilbertIndex on first access, cached thereafter).
    /// Range: [0, 2^Precision - 1]
    /// </summary>
    public double X
    {
        get
        {
            if (!_cachedX.HasValue)
            {
                var (x, y, z) = HilbertCurve.Decode(HilbertIndex, Precision);
                _cachedX = x;
                _cachedY = y;
                _cachedZ = z;
            }
            return _cachedX.Value;
        }
    }

    /// <summary>
    /// Y coordinate (decoded from HilbertIndex on first access, cached thereafter).
    /// Range: [0, 2^Precision - 1]
    /// </summary>
    public double Y
    {
        get
        {
            if (!_cachedY.HasValue)
            {
                var (x, y, z) = HilbertCurve.Decode(HilbertIndex, Precision);
                _cachedX = x;
                _cachedY = y;
                _cachedZ = z;
            }
            return _cachedY.Value;
        }
    }

    /// <summary>
    /// Z coordinate (decoded from HilbertIndex on first access, cached thereafter).
    /// Range: [0, 2^Precision - 1]
    /// </summary>
    public double Z
    {
        get
        {
            if (!_cachedZ.HasValue)
            {
                var (x, y, z) = HilbertCurve.Decode(HilbertIndex, Precision);
                _cachedX = x;
                _cachedY = y;
                _cachedZ = z;
            }
            return _cachedZ.Value;
        }
    }

    // Private constructor for Hilbert-first creation
    private SpatialCoordinate(ulong hilbertIndex, int precision)
    {
        if (precision < 1 || precision > HilbertCurve.MaxPrecision)
            throw new ArgumentException($"Precision must be between 1 and {HilbertCurve.MaxPrecision}", nameof(precision));

        HilbertIndex = hilbertIndex;
        Precision = precision;
    }

    // Private constructor for Cartesian creation (encodes to Hilbert immediately)
    private SpatialCoordinate(double x, double y, double z, int precision)
    {
        if (precision < 1 || precision > HilbertCurve.MaxPrecision)
            throw new ArgumentException($"Precision must be between 1 and {HilbertCurve.MaxPrecision}", nameof(precision));

        Precision = precision;
        HilbertIndex = HilbertCurve.Encode(x, y, z, precision);

        // Cache the original coordinates to avoid decode round-trip
        _cachedX = x;
        _cachedY = y;
        _cachedZ = z;
    }

    /// <summary>
    /// Creates a spatial coordinate from Hilbert index (PRIMARY constructor).
    /// Cartesian coordinates (X, Y, Z) will be decoded on-demand.
    /// This is the most efficient constructor for database retrieval.
    /// </summary>
    public static SpatialCoordinate FromHilbert(ulong hilbertIndex, int precision = HilbertCurve.DefaultPrecision)
    {
        return new SpatialCoordinate(hilbertIndex, precision);
    }

    /// <summary>
    /// Creates a spatial coordinate from Cartesian coordinates.
    /// Hilbert index is computed immediately and becomes the source of truth.
    /// Use this for content ingestion when X,Y,Z are the natural input.
    /// </summary>
    public static SpatialCoordinate Create(double x, double y, double z, int precision = HilbertCurve.DefaultPrecision)
    {
        return new SpatialCoordinate(x, y, z, precision);
    }

    /// <summary>
    /// Creates spatial coordinate from hash using deterministic mapping (legacy compatibility).
    /// Maps hash bytes to normalized space [0, 2^21-1], then encodes to Hilbert.
    /// </summary>
    public static SpatialCoordinate FromHash(Hash256 hash, int precision = HilbertCurve.DefaultPrecision)
    {
        if (hash == null)
            throw new ArgumentNullException(nameof(hash));

        var bytes = hash.Bytes;

        // Use first 24 bytes (8 bytes per coordinate) for maximum precision
        var x = MapBytesToCoordinate(bytes, 0, precision);
        var y = MapBytesToCoordinate(bytes, 8, precision);
        var z = MapBytesToCoordinate(bytes, 16, precision);

        return Create(x, y, z, precision);
    }

    private static double MapBytesToCoordinate(byte[] bytes, int offset, int precision)
    {
        // Convert 8 bytes to ulong
        var value = BitConverter.ToUInt64(bytes, offset);

        // Map [0, UInt64.MaxValue] to [0, 2^precision - 1]
        ulong maxValue = (1UL << precision) - 1;
        return (value / (double)ulong.MaxValue) * maxValue;
    }
    
    private static bool IsValidCoordinate(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }
    
    /// <summary>
    /// Calculates Euclidean distance using decoded Cartesian coordinates.
    /// For large-scale approximate queries, use HilbertDistanceTo instead (much faster).
    /// </summary>
    public double DistanceTo(SpatialCoordinate other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    
    /// <summary>
    /// Calculates Manhattan distance using decoded Cartesian coordinates.
    /// </summary>
    public double ManhattanDistanceTo(SpatialCoordinate other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        
        return Math.Abs(X - other.X) + Math.Abs(Y - other.Y) + Math.Abs(Z - other.Z);
    }

    /// <summary>
    /// Calculates approximate distance using Hilbert index proximity.
    /// MUCH FASTER than Euclidean distance for large-scale queries (no decoding required).
    /// Use for initial filtering in k-NN queries, then refine with DistanceTo for exact results.
    /// </summary>
    public ulong HilbertDistanceTo(SpatialCoordinate other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        // Hilbert distance is the absolute difference in indices
        // Locality preservation: nearby 3D points have nearby Hilbert indices
        return HilbertIndex > other.HilbertIndex 
            ? HilbertIndex - other.HilbertIndex 
            : other.HilbertIndex - HilbertIndex;
    }

    /// <summary>
    /// Returns the decoded Cartesian coordinates as a tuple.
    /// Use when you need all three coordinates at once.
    /// </summary>
    public (double X, double Y, double Z) ToCartesian()
    {
        return (X, Y, Z);
    }

    /// <summary>
    /// Gets the Hilbert index range for k-NN queries within given radius.
    /// Returns (minIndex, maxIndex) for fast B-tree range query.
    /// </summary>
    public (ulong MinIndex, ulong MaxIndex) GetHilbertRangeForRadius(double radius)
    {
        return HilbertCurve.GetRangeForRadius(HilbertIndex, radius, Precision);
    }

    /// <summary>
    /// Interpolate (average) multiple spatial coordinates to create a centroid.
    /// Uses Cartesian averaging, then re-encodes to Hilbert index.
    /// Useful for BPE token spatial representation from constituent constants.
    /// </summary>
    public static SpatialCoordinate Interpolate(IEnumerable<SpatialCoordinate> coordinates, int? precision = null)
    {
        var coordList = coordinates.ToList();
        if (coordList.Count == 0)
            throw new ArgumentException("Cannot interpolate empty coordinate list", nameof(coordinates));

        if (coordList.Count == 1)
            return coordList[0];

        // Use first coordinate's precision if not specified
        var targetPrecision = precision ?? coordList[0].Precision;

        // Average Cartesian coordinates
        double sumX = 0, sumY = 0, sumZ = 0;
        foreach (var coord in coordList)
        {
            sumX += coord.X;
            sumY += coord.Y;
            sumZ += coord.Z;
        }

        var avgX = sumX / coordList.Count;
        var avgY = sumY / coordList.Count;
        var avgZ = sumZ / coordList.Count;

        return Create(avgX, avgY, avgZ, targetPrecision);
    }
    
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        // Equality based on HilbertIndex (source of truth), not decoded coordinates
        yield return HilbertIndex;
        yield return Precision;
    }
    
    public override string ToString() => $"H:{HilbertIndex} ({X:F2}, {Y:F2}, {Z:F2})";
}
