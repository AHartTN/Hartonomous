using System;
using System.Runtime.InteropServices;

namespace Hartonomous.Marshal;

/// <summary>
/// 4D Hilbert space-filling curve implementation using Skilling's Algorithm.
/// Maps 4D coordinates (X, Y, Z, M) to a 1D index (split into High/Low) while preserving locality.
/// 
/// Implementation:
/// - Uses John Skilling's "Programming the Hilbert curve" (2004) algebraic method.
/// - Native C++ implementation for performance.
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
        x = Math.Min(x, maxCoordValue);
        y = Math.Min(y, maxCoordValue);
        z = Math.Min(z, maxCoordValue);
        m = Math.Min(m, maxCoordValue);

        NativeMethods.HilbertEncode4D(x, y, z, m, precision, out ulong resultHigh, out ulong resultLow);
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
    
    /// <summary>
    /// Calculates the distance between two Hilbert indices (treats as unsigned 128-bit integers).
    /// </summary>
    public static ulong Distance((ulong High, ulong Low) index1, (ulong High, ulong Low) index2)
    {
        // Treat as 128-bit unsigned integers
        // For simplicity, we calculate distance based on the high bits primarily
        if (index1.High != index2.High)
        {
            return index1.High > index2.High 
                ? index1.High - index2.High 
                : index2.High - index1.High;
        }
        
        // If high bits are equal, use low bits
        return index1.Low > index2.Low 
            ? index1.Low - index2.Low 
            : index2.Low - index1.Low;
    }
    
    /// <summary>
    /// Gets the Hilbert index range for a given radius around a center point.
    /// </summary>
    public static ((ulong High, ulong Low) Min, (ulong High, ulong Low) Max) GetRangeForRadius(
        (ulong High, ulong Low) center,
        ulong radius)
    {
        // Simple approach: subtract/add radius from center
        // Note: This is a simplified version; proper implementation would handle overflow
        var minLow = center.Low > radius ? center.Low - radius : 0;
        var maxLow = center.Low < (ulong.MaxValue - radius) ? center.Low + radius : ulong.MaxValue;
        
        return ((center.High, minLow), (center.High, maxLow));
    }
    
    /// <summary>
    /// Gets the Hilbert index range for a given radius around a center point.
    /// 4-parameter overload for compatibility with Core.
    /// </summary>
    public static ((ulong High, ulong Low) Min, (ulong High, ulong Low) Max) GetRangeForRadius(
        ulong high, ulong low, ulong radius, int precision)
    {
        // Call the 2-parameter version, ignoring precision for now
        return GetRangeForRadius((high, low), radius);
    }
    
    /// <summary>
    /// Extracts the tile ID (top N bits) from a full Hilbert index.
    /// This is used for hierarchical spatial partitioning (quadtree/octree-like structure).
    /// </summary>
    /// <param name="high">Upper 42 bits of full Hilbert index</param>
    /// <param name="low">Lower 42 bits of full Hilbert index</param>
    /// <param name="level">Number of bits per dimension to keep (tile resolution)</param>
    /// <param name="precision">Original precision used to encode the index</param>
    /// <returns>Tile ID with top 'level' bits per dimension, rest zeroed</returns>
    public static (ulong High, ulong Low) GetHilbertTileId(
        ulong high, ulong low, int level, int precision = DefaultPrecision)
    {
        if (level < 0 || level > precision)
            throw new ArgumentException($"Level must be between 0 and {precision}", nameof(level));
        
        if (precision < 1 || precision > MaxPrecision)
            throw new ArgumentException($"Precision must be between 1 and {MaxPrecision}", nameof(precision));
        
        // Total bits in Hilbert index = 4 * precision (e.g., 84 bits for precision=21)
        int totalBits = 4 * precision;
        
        // Tile level specifies how many bits per dimension to keep
        // For level L, we keep the top (4 * L) bits and zero the rest
        int bitsToKeep = 4 * level;
        int bitsToZero = totalBits - bitsToKeep;
        
        if (bitsToZero <= 0)
        {
            // Keep all bits (no masking needed)
            return (high, low);
        }
        
        // The 84-bit index is split: high (42 bits) | low (42 bits)
        // We need to zero out the bottom 'bitsToZero' bits
        
        if (bitsToZero >= 42)
        {
            // Zero all of low, and some of high
            int highBitsToZero = bitsToZero - 42;
            ulong highMask = ~0UL << highBitsToZero;
            return (high & highMask, 0);
        }
        else
        {
            // Zero some of low, keep all of high
            ulong lowMask = ~0UL << bitsToZero;
            return (high, low & lowMask);
        }
    }
}
