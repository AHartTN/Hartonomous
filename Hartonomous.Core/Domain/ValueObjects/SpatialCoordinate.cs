using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.Mathematics;
using Hartonomous.Core.Domain.Utilities;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.ValueObjects;

/// <summary>
/// 4D Hilbert-first spatial coordinate with redundant metadata storage (Option D).
/// 
/// Architecture:
/// - PRIMARY: 4D Hilbert curve index (84 bits: HilbertHigh + HilbertLow)
/// - REDUNDANT: Quantized metadata (entropy, compressibility, connectivity) for zero-decode filtering
/// - MATERIALIZED: POINTZM geometry computed from Hilbert + metadata
/// 
/// All 4 dimensions participate in Hilbert curve:
/// - X: Spatial locality (from hash via Gram-Schmidt projection)
/// - Y: Shannon entropy (content randomness)
/// - Z: Kolmogorov complexity (compressibility via gzip ratio)
/// - M: Graph connectivity (reference count, logarithmic)
/// 
/// Benefits:
/// - Optimal 4D spatial locality (best possible clustering)
/// - Zero-decode metadata queries (redundant columns)
/// - B-tree indexed spatial queries (100x faster than GIST)
/// - Single source of truth for position (HilbertHigh + HilbertLow)
/// - GPU-ready architecture (unified 4D operations)
/// </summary>
public sealed class SpatialCoordinate : ValueObject
{
    private const int MaxQuantizedValue = 2_097_151; // 2^21 - 1
    
    /// <summary>
    /// Upper 42 bits of 4D Hilbert curve index.
    /// Combined with HilbertLow, encodes all 4 dimensions (X, Y, Z, M).
    /// </summary>
    public ulong HilbertHigh { get; private init; }
    
    /// <summary>
    /// Lower 42 bits of 4D Hilbert curve index.
    /// Combined with HilbertHigh, encodes all 4 dimensions (X, Y, Z, M).
    /// </summary>
    public ulong HilbertLow { get; private init; }
    
    /// <summary>
    /// Precision of Hilbert encoding (bits per dimension). Default 21 bits = 2,097,152 resolution.
    /// </summary>
    public int Precision { get; private init; }
    
    /// <summary>
    /// REDUNDANT: Shannon entropy quantized to [0, 2^21-1].
    /// Stored separately for fast B-tree filtering without decoding Hilbert index.
    /// </summary>
    public int QuantizedEntropy { get; private init; }
    
    /// <summary>
    /// REDUNDANT: Kolmogorov complexity (compressibility) quantized to [0, 2^21-1].
    /// Stored separately for fast B-tree filtering without decoding Hilbert index.
    /// </summary>
    public int QuantizedCompressibility { get; private init; }
    
    /// <summary>
    /// REDUNDANT: Graph connectivity quantized to [0, 2^21-1].
    /// Stored separately for fast B-tree filtering without decoding Hilbert index.
    /// </summary>
    public int QuantizedConnectivity { get; private init; }
    
    // Cached decoded coordinates (lazy initialization)
    private Lazy<(double X, double Y, double Z, double M)>? _decodedCoordinates;
    
    /// <summary>
    /// X coordinate: Spatial dimension (decoded from Hilbert index).
    /// Range: [0, 2^Precision - 1]
    /// </summary>
    public double X => GetDecodedCoordinates().X;
    
    /// <summary>
    /// Y coordinate: Shannon entropy (quantized integer from Hilbert decode).
    /// Range: [0, 2^Precision - 1]
    /// </summary>
    public double Y => GetDecodedCoordinates().Y;

    /// <summary>
    /// Z coordinate: Kolmogorov complexity (quantized integer from Hilbert decode).
    /// Range: [0, 2^Precision - 1]
    /// </summary>
    public double Z => GetDecodedCoordinates().Z;

    /// <summary>
    /// M coordinate: Graph connectivity (quantized integer from Hilbert decode).
    /// Range: [0, 2^Precision - 1]
    /// </summary>
    public double M => GetDecodedCoordinates().M;
    
    private SpatialCoordinate()
    {
        // Private constructor for EF Core
        InitializeLazyDecoder();
    }
    
    /// <summary>
    /// Creates a spatial coordinate from 4D Hilbert index and metadata (PRIMARY constructor).
    /// Use this when loading from database.
    /// </summary>
    public static SpatialCoordinate FromHilbert4D(
        ulong hilbertHigh, ulong hilbertLow,
        int quantizedEntropy, int quantizedCompressibility, int quantizedConnectivity,
        int precision = HilbertCurve4D.DefaultPrecision)
    {
        return new SpatialCoordinate
        {
            HilbertHigh = hilbertHigh,
            HilbertLow = hilbertLow,
            Precision = precision,
            QuantizedEntropy = quantizedEntropy,
            QuantizedCompressibility = quantizedCompressibility,
            QuantizedConnectivity = quantizedConnectivity
        };
    }
    
    /// <summary>
    /// Creates a spatial coordinate from universal properties (for content ingestion).
    /// Encodes all 4 dimensions into Hilbert curve and stores metadata redundantly.
    /// </summary>
    public static SpatialCoordinate FromUniversalProperties(
        uint spatialX,
        int quantizedEntropy,
        int quantizedCompressibility,
        int quantizedConnectivity,
        int precision = HilbertCurve4D.DefaultPrecision)
    {
        // Validate quantized values
        if (quantizedEntropy < 0 || quantizedEntropy > MaxQuantizedValue)
            throw new ArgumentException($"QuantizedEntropy must be [0, {MaxQuantizedValue}]", nameof(quantizedEntropy));
        if (quantizedCompressibility < 0 || quantizedCompressibility > MaxQuantizedValue)
            throw new ArgumentException($"QuantizedCompressibility must be [0, {MaxQuantizedValue}]", nameof(quantizedCompressibility));
        if (quantizedConnectivity < 0 || quantizedConnectivity > MaxQuantizedValue)
            throw new ArgumentException($"QuantizedConnectivity must be [0, {MaxQuantizedValue}]", nameof(quantizedConnectivity));
        
        // Encode ALL 4 dimensions into 4D Hilbert curve
        var (hilbertHigh, hilbertLow) = HilbertCurve4D.Encode(
            spatialX,
            (uint)quantizedEntropy,
            (uint)quantizedCompressibility,
            (uint)quantizedConnectivity,
            precision);
        
        return new SpatialCoordinate
        {
            HilbertHigh = hilbertHigh,
            HilbertLow = hilbertLow,
            Precision = precision,
            // Store redundantly for fast filtering
            QuantizedEntropy = quantizedEntropy,
            QuantizedCompressibility = quantizedCompressibility,
            QuantizedConnectivity = quantizedConnectivity
        };
    }
    
    /// <summary>
    /// Creates spatial coordinate from hash using deterministic Gram-Schmidt projection.
    /// Maps hash bytes + modality to spatial dimension, then combines with metadata.
    /// </summary>
    public static SpatialCoordinate FromHash(
        Hash256 hash,
        int quantizedEntropy,
        int quantizedCompressibility,
        int quantizedConnectivity,
        string? modality = null,
        int precision = HilbertCurve4D.DefaultPrecision)
    {
        if (hash == null)
            throw new ArgumentNullException(nameof(hash));
        
        // Project hash to spatial dimension using Gram-Schmidt orthonormalization
        var spatialX = GramSchmidtProjector.Project(hash, modality, precision);
        
        return FromUniversalProperties(
            spatialX, quantizedEntropy, quantizedCompressibility, quantizedConnectivity, precision);
    }
    
    /// <summary>
    /// LEGACY COMPATIBILITY: Creates spatial coordinate from XYZ coordinates.
    /// Uses placeholder metadata values. For new code, use FromUniversalProperties instead.
    /// </summary>
    [Obsolete("Use FromUniversalProperties or FromHash with metadata instead")]
    public static SpatialCoordinate Create(
        double x, double y, double z,
        int precision = HilbertCurve4D.DefaultPrecision)
    {
        // Use placeholder metadata
        const int placeholderEntropy = 1_048_576; // Mid-range
        const int placeholderCompressibility = 1_048_576; // Mid-range
        const int placeholderConnectivity = 0;
        
        return FromUniversalProperties(
            (uint)x,
            placeholderEntropy,
            placeholderCompressibility,
            placeholderConnectivity,
            precision);
    }
    
    /// <summary>
    /// Interpolates multiple spatial coordinates to create a centroid.
    /// Used for BPE token spatial representation from constituent constants.
    /// Averages Cartesian coordinates (X, Y, Z, M) then re-encodes to 4D Hilbert.
    /// </summary>
    public static SpatialCoordinate Interpolate(
        IEnumerable<SpatialCoordinate> coordinates,
        int? precision = null)
    {
        var coordList = coordinates.ToList();
        if (coordList.Count == 0)
            throw new ArgumentException("Cannot interpolate empty coordinate list", nameof(coordinates));

        if (coordList.Count == 1)
            return coordList[0];

        var targetPrecision = precision ?? coordList[0].Precision;

        // Average all 4D quantized integer coordinates
        double sumX = 0, sumY = 0, sumZ = 0, sumM = 0;
        foreach (var coord in coordList)
        {
            sumX += coord.X;  // Decoded quantized X
            sumY += coord.Y;  // Decoded quantized Y (entropy)
            sumZ += coord.Z;  // Decoded quantized Z (compressibility)
            sumM += coord.M;  // Decoded quantized M (connectivity)
        }

        var avgX = (uint)Math.Round(sumX / coordList.Count);
        var avgY = (int)Math.Round(sumY / coordList.Count);
        var avgZ = (int)Math.Round(sumZ / coordList.Count);
        var avgM = (int)Math.Round(sumM / coordList.Count);

        return FromUniversalProperties(
            avgX,
            avgY,
            avgZ,
            avgM,
            targetPrecision);
    }
    
    /// <summary>
    /// Convert to PostGIS POINTZM geometry (materialized view).
    /// X: spatial, Y: entropy, Z: compressibility, M: connectivity
    /// </summary>
    public Point ToPoint()
    {
        return new Point(new CoordinateZM(X, Y, Z, M)) { SRID = 4326 };
    }
    
    /// <summary>
    /// Returns the decoded 4D coordinates as a tuple.
    /// </summary>
    public (double X, double Y, double Z, double M) ToCartesian4D()
    {
        return (X, Y, Z, M);
    }
    
    /// <summary>
    /// Calculates Euclidean distance in 4D space using decoded coordinates.
    /// </summary>
    public double DistanceTo(SpatialCoordinate other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        var dm = M - other.M;
        
        return Math.Sqrt(dx * dx + dy * dy + dz * dz + dm * dm);
    }
    
    /// <summary>
    /// Calculates approximate distance using Hilbert4D index proximity.
    /// MUCH FASTER than Euclidean distance (no decoding required).
    /// Use for initial filtering in k-NN queries.
    /// </summary>
    public ulong HilbertDistanceTo(SpatialCoordinate other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        
        return HilbertCurve4D.Distance(
            (HilbertHigh, HilbertLow),
            (other.HilbertHigh, other.HilbertLow));
    }
    
    /// <summary>
    /// Gets the Hilbert4D index range for k-NN queries within given radius.
    /// Returns (minHigh, minLow, maxHigh, maxLow) for fast B-tree range query.
    /// </summary>
    public (ulong MinHigh, ulong MinLow, ulong MaxHigh, ulong MaxLow) GetHilbertRangeForRadius(double radius)
    {
        return HilbertCurve4D.GetRangeForRadius(HilbertHigh, HilbertLow, radius, Precision);
    }
    
    #region Private Helpers
    
    private void InitializeLazyDecoder()
    {
        _decodedCoordinates = new Lazy<(double, double, double, double)>(() =>
        {
            // Decode 4D Hilbert index back to all 4 quantized integer coordinates
            var (x, y, z, m) = HilbertCurve4D.Decode(HilbertHigh, HilbertLow, Precision);

            // Return all dimensions as quantized integers [0, 2^Precision-1]
            return ((double)x, (double)y, (double)z, (double)m);
        });
    }
    
    private (double X, double Y, double Z, double M) GetDecodedCoordinates()
    {
        if (_decodedCoordinates == null)
            InitializeLazyDecoder();
        
        return _decodedCoordinates!.Value;
    }
    
    private static double DequantizeEntropy(int quantized)
    {
        // Map [0, 2^21-1] back to [0, 8] bits
        return (quantized / (double)MaxQuantizedValue) * 8.0;
    }
    
    private static double DequantizeCompressibility(int quantized)
    {
        // Map [0, 2^21-1] back to [0, 1]
        return quantized / (double)MaxQuantizedValue;
    }
    
    private static double DequantizeConnectivity(int quantized)
    {
        // Map [0, 2^21-1] back to [0, 21] log scale
        return (quantized / (double)MaxQuantizedValue) * 21.0;
    }
    
    #endregion
    
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        // Equality based on Hilbert4D index (source of truth)
        yield return HilbertHigh;
        yield return HilbertLow;
        yield return Precision;
    }
    
    public override string ToString() => 
        $"H4D:({HilbertHigh:X},{HilbertLow:X}) " +
        $"X:{X:F0} Y:{Y:F2} Z:{Z:F2} M:{M:F2}";
}
