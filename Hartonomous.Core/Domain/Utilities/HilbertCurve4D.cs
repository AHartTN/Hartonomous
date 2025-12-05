using System;
using System.Runtime.InteropServices;

namespace Hartonomous.Core.Domain.Utilities;

/// <summary>
/// 4D Hilbert space-filling curve implementation using Skilling's Algorithm.
/// Maps 4D coordinates (X, Y, Z, M) to a 1D index (split into High/Low) while preserving locality.
/// 
/// Implementation:
/// - Uses John Skilling's "Programming the Hilbert curve" (2004) algebraic method.
/// - Avoids massive lookup tables (384 entries) by computing symmetry transforms on-the-fly.
/// - Precision: Up to 21 bits per dimension (84 bits total).
/// - Coordinates are 0-based integers [0, 2^Precision - 1].
/// </summary>
public static class HilbertCurve4D
{
    /// <summary>
    /// Default precision: 21 bits per dimension
    /// Provides 2,097,152 resolution per axis (2^21)
    /// </summary>
    public const int DefaultPrecision = 21;
    
    /// <summary>
    /// Maximum supported precision: 21 bits per dimension
    /// Total: 84 bits (21 * 4) split across two ulongs
    /// </summary>
    public const int MaxPrecision = 21;
    
    /// <summary>
    /// Number of dimensions
    /// </summary>
    public const int Dimensions = 4;
    
    /// <summary>
    /// Encodes 4D coordinates to Hilbert curve index using the native implementation.
    /// </summary>
    /// <param name="x">X coordinate [0, 2^precision - 1]</param>
    /// <param name="y">Y coordinate [0, 2^precision - 1]</param>
    /// <param name="z">Z coordinate [0, 2^precision - 1]</param>
    /// <param name="m">M coordinate [0, 2^precision - 1]</param>
    /// <param name="precision">Bits per dimension (1-21, default 21)</param>
    /// <returns>Tuple of (High 42 bits, Low 42 bits)</returns>
    public static (ulong High, ulong Low) Encode(
        uint x, uint y, uint z, uint m,
        int precision = DefaultPrecision)
    {
        if (precision < 1 || precision > MaxPrecision)
            throw new ArgumentException($"Precision must be between 1 and {MaxPrecision}", nameof(precision));
        
        // Clamp coordinates to the maximum value for the given precision
        uint maxCoordValue = (1u << precision) - 1;
        x = System.Math.Min(x, maxCoordValue);
        y = System.Math.Min(y, maxCoordValue);
        z = System.Math.Min(z, maxCoordValue);
        m = System.Math.Min(m, maxCoordValue);

        ulong resultHigh, resultLow;
        NativeMethods.HilbertEncode4D(x, y, z, m, precision, out resultHigh, out resultLow);
        return (resultHigh, resultLow);
    }
    
    /// <summary>
    /// Decodes Hilbert curve index back to 4D coordinates using the native implementation.
    /// </summary>
    /// <param name="high">Upper 42 bits of Hilbert index</param>
    /// <param name="low">Lower 42 bits of Hilbert index</param>
    /// <param name="precision">Bits per dimension used during encoding</param>
    /// <returns>Tuple of (X, Y, Z, M) coordinates</returns>
    public static (uint X, uint Y, uint Z, uint M) Decode(
        ulong high, ulong low,
        int precision = DefaultPrecision)
    {
        if (precision < 1 || precision > MaxPrecision)
            throw new ArgumentException($"Precision must be between 1 and {MaxPrecision}", nameof(precision));
        
        // Allocate an array to hold the decoded coordinates
        uint[] decodedCoords = new uint[Dimensions];
        
        // Pin the array to prevent the garbage collector from moving it
        GCHandle gcHandle = GCHandle.Alloc(decodedCoords, GCHandleType.Pinned);
        try
        {
            // Pass the pointer to the native function
            NativeMethods.HilbertDecode4D(high, low, precision, gcHandle.AddrOfPinnedObject());
            
            // Extract the decoded coordinates from the array
            return (decodedCoords[0], decodedCoords[1], decodedCoords[2], decodedCoords[3]);
        }
        finally
        {
            // Free the GCHandle
            if (gcHandle.IsAllocated)
            {
                gcHandle.Free();
            }
        }
    }
    
    // Moved helper methods here for organization

    private static uint GrayToBinary(uint gray)
    {
        uint mask = gray >> 1;
        while (mask != 0)
        {
            gray ^= mask;
            mask >>= 1;
        }
        return gray;
    }

    private static void SetBit(ref ulong high, ref ulong low, int bitPos, ulong bitVal)
    {
        int shift;
        if (bitPos >= 42)
        {
            shift = bitPos - 42;
            if (bitVal == 1) high |= (1UL << shift);
            else high &= ~(1UL << shift);
        }
        else
        {
            shift = bitPos;
            if (bitVal == 1) low |= (1UL << shift);
            else low &= ~(1UL << shift);
        }
    }

    private static ulong GetBit(ulong high, ulong low, int bitPos)
    {
        int shift;
        if (bitPos >= 42)
        {
            shift = bitPos - 42;
            return (high >> shift) & 1;
        }
        else
        {
            shift = bitPos;
            return (low >> shift) & 1;
        }
    }

    /// <summary>
    /// Approximate distance using Hilbert index proximity.
    /// </summary>
    public static ulong Distance((ulong High, ulong Low) index1, (ulong High, ulong Low) index2)
    {
        ulong highDiff = index1.High > index2.High 
            ? index1.High - index2.High 
            : index2.High - index1.High;
            
        if (highDiff > 0)
        {
            return highDiff << 42; // Approximation
        }
        
        return index1.Low > index2.Low 
            ? index1.Low - index2.Low 
            : index2.Low - index1.Low;
    }

    /// <summary>
    /// Gets the Hilbert tile ID (prefix) for a given full Hilbert index at a specified tile precision level.
    /// This effectively truncates the Hilbert index to define a larger, coarser tile.
    /// </summary>
    /// <param name="high">Upper 42 bits of the full Hilbert index.</param>
    /// <param name="low">Lower 42 bits of the full Hilbert index.</param>
    /// <param name="tilePrecision">The desired precision (bits per dimension) for the tile ID.
    /// Must be less than or equal to the original encoding precision. Max 21.</param>
    /// <param name="originalPrecision">The original precision used to encode the full Hilbert index.</param>
    /// <returns>A tuple representing the Hilbert index of the tile (High, Low).</returns>
    public static (ulong High, ulong Low) GetHilbertTileId(
        ulong high, ulong low,
        int tilePrecision,
        int originalPrecision = DefaultPrecision)
    {
        if (tilePrecision < 1 || tilePrecision > originalPrecision || tilePrecision > MaxPrecision)
            throw new ArgumentException($"Tile precision must be between 1 and {originalPrecision} (max {MaxPrecision})", nameof(tilePrecision));

        // Decode to get the coordinates at the original precision
        var (x, y, z, m) = Decode(high, low, originalPrecision);

        // Re-encode using the lower tilePrecision.
        // This creates a new Hilbert index that represents the "tile" or prefix.
        return Encode(x >> (originalPrecision - tilePrecision),
                      y >> (originalPrecision - tilePrecision),
                      z >> (originalPrecision - tilePrecision),
                      m >> (originalPrecision - tilePrecision),
                      tilePrecision);
    }

    /// <summary>
    /// Get range of Hilbert indices covering a 4D radius.
    /// </summary>
    public static (ulong MinHigh, ulong MinLow, ulong MaxHigh, ulong MaxLow) GetRangeForRadius(
        ulong centerHigh, ulong centerLow,
        double radius,
        int precision = DefaultPrecision)
    {
        var (cx, cy, cz, cm) = Decode(centerHigh, centerLow, precision);
        uint r = (uint)System.Math.Ceiling(radius);
        long maxVal = (1L << precision) - 1;

        // Cast everything to long for safe arithmetic and comparison
        // Use System.Math explicitly
        uint minX = (uint)System.Math.Max(0L, (long)cx - r);
        uint maxX = (uint)System.Math.Min(maxVal, (long)cx + r);
        
        uint minY = (uint)System.Math.Max(0L, (long)cy - r);
        uint maxY = (uint)System.Math.Min(maxVal, (long)cy + r);
        
        uint minZ = (uint)System.Math.Max(0L, (long)cz - r);
        uint maxZ = (uint)System.Math.Min(maxVal, (long)cz + r);
        
        uint minM = (uint)System.Math.Max(0L, (long)cm - r);
        uint maxM = (uint)System.Math.Min(maxVal, (long)cm + r);

        var start = Encode(minX, minY, minZ, minM, precision);
        var end = Encode(maxX, maxY, maxZ, maxM, precision);
        
        if (start.High > end.High || (start.High == end.High && start.Low > end.Low))
        {
            return (end.High, end.Low, start.High, start.Low);
        }
        return (start.High, start.Low, end.High, end.Low);
    }
}