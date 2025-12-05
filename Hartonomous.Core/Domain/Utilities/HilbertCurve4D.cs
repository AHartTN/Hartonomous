using System;

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
    /// Encodes 4D coordinates to Hilbert curve index.
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
        
        // 1. Binary to Gray Code
        x ^= x >> 1;
        y ^= y >> 1;
        z ^= z >> 1;
        m ^= m >> 1;

        // 2. Transpose (Skilling's Transform)
        // Rotates the coordinate system to align with the curve traversal
        (x, y, z, m) = Transpose(x, y, z, m, precision);

        // 3. Interleave bits into 1D index (84 bits total)
        // High ulong gets bits 42-83 (42 bits)
        // Low ulong gets bits 0-41 (42 bits)
        ulong high = 0;
        ulong low = 0;

        for (int i = precision - 1; i >= 0; i--)
        {
            // We pack 4 bits per iteration (one from each dimension)
            // Total bits processed so far: (precision - 1 - i) * 4
            
            // Bit values at position i
            ulong bx = (x >> i) & 1;
            ulong by = (y >> i) & 1;
            ulong bz = (z >> i) & 1;
            ulong bm = (m >> i) & 1;

            int bitPos = i * 4; // Position in the full 84-bit stream (0 = LSB)
            
            // Map to High/Low ulongs
            // Split point is bit 42.
            // Bits 0-41 -> Low
            // Bits 42-83 -> High
            
            // Bit 0 (M):
            SetBit(ref high, ref low, bitPos + 0, bm);
            // Bit 1 (Z):
            SetBit(ref high, ref low, bitPos + 1, bz);
            // Bit 2 (Y):
            SetBit(ref high, ref low, bitPos + 2, by);
            // Bit 3 (X):
            SetBit(ref high, ref low, bitPos + 3, bx);
        }

        return (high, low);
    }
    
    /// <summary>
    /// Decodes Hilbert curve index back to 4D coordinates.
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
        
        uint x = 0, y = 0, z = 0, m = 0;

        // 1. De-interleave bits
        for (int i = 0; i < precision; i++)
        {
            int bitPos = i * 4;
            
            uint bm = (uint)GetBit(high, low, bitPos + 0);
            uint bz = (uint)GetBit(high, low, bitPos + 1);
            uint by = (uint)GetBit(high, low, bitPos + 2);
            uint bx = (uint)GetBit(high, low, bitPos + 3);

            x |= bx << i;
            y |= by << i;
            z |= bz << i;
            m |= bm << i;
        }

        // 2. Untranspose (Inverse Skilling's Transform)
        (x, y, z, m) = Transpose(x, y, z, m, precision);

        // 3. Gray Code to Binary
        x = GrayToBinary(x);
        y = GrayToBinary(y);
        z = GrayToBinary(z);
        m = GrayToBinary(m);

        return (x, y, z, m);
    }

    /// <summary>
    /// Skilling's "Transpose" operation.
    /// Swaps bits between dimensions based on higher-order bits to effect the rotation/reflection.
    /// This implements the 384 symmetries algebraically.
    /// </summary>
    private static (uint x, uint y, uint z, uint m) Transpose(uint x, uint y, uint z, uint m, int precision)
    {
        uint[] X = { x, y, z, m };
        
        // Standard Skilling Loop
        for (int b = precision - 1; b >= 0; b--)
        {
            uint P = 1u << b;
            
            // Inverse X[i] if X[n-1] (last dim, 'm') has bit b set
            if ((X[3] & P) != 0)
            {
                X[0] ^= (P - 1); // Invert lower bits
                X[1] ^= (P - 1);
                X[2] ^= (P - 1);
            }
            
            // Rotate/Swap if X[n-2] (z) has bit b set
            if ((X[2] & P) != 0)
            {
                uint temp = X[0];
                X[0] = X[3];
                X[3] = temp;
                
                X[0] ^= (P - 1);
                X[1] ^= (P - 1);
            }
            
            // Rotate/Swap if X[n-3] (y) has bit b set
            if ((X[1] & P) != 0)
            {
                uint temp = X[0];
                X[0] = X[2];
                X[2] = temp;
                
                X[0] ^= (P - 1);
            }
            
            // Rotate/Swap if X[0] (x) bit set
            if ((X[0] & P) != 0)
            {
                uint temp = X[0];
                X[0] = X[1];
                X[1] = temp;
            }
        }

        return (X[0], X[1], X[2], X[3]);
    }

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
        if (bitPos >= 42)
        {
            int shift = bitPos - 42;
            if (bitVal == 1) high |= (1UL << shift);
            else high &= ~(1UL << shift);
        }
        else
        {
            if (bitVal == 1) low |= (1UL << bitPos);
            else low &= ~(1UL << bitPos);
        }
    }

    private static ulong GetBit(ulong high, ulong low, int bitPos)
    {
        if (bitPos >= 42)
        {
            return (high >> (bitPos - 42)) & 1;
        }
        else
        {
            return (low >> bitPos) & 1;
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

        // Calculate how many bits to keep from the highest precision (originalPrecision)
        // Each dimension contributes 'originalPrecision' bits.
        // We want to keep 'tilePrecision' bits from each dimension.
        // So, we need to mask out (originalPrecision - tilePrecision) bits from each dimension.
        
        // Decode to get the coordinates at the original precision
        var (x, y, z, m) = Decode(high, low, originalPrecision);

        // Mask off the lower bits of each coordinate to get the tile's coordinates
        // The mask should keep only the top 'tilePrecision' bits.
        // E.g., if originalPrecision=21, tilePrecision=10, we keep top 10 bits.
        uint mask = (1u << tilePrecision) - 1; // Creates 0b11...1 (tilePrecision times)
        
        // This is wrong. It should be: (coord >> (originalPrecision - tilePrecision)) << (originalPrecision - tilePrecision)
        // No, just truncate and re-encode. The effect of masking bits in the coordinate
        // is re-encoding with lower precision.
        
        // Re-encode using the lower tilePrecision.
        // This creates a new Hilbert index that represents the "tile" or prefix.
        return Encode(x >> (originalPrecision - tilePrecision),
                      y >> (originalPrecision - tilePrecision),
                      z >> (originalPrecision - tilePrecision),
                      m >> (originalPrecision - tilePrecision),
                      tilePrecision);
    }
    /// </summary>
    public static (ulong MinHigh, ulong MinLow, ulong MaxHigh, ulong MaxLow) GetRangeForRadius(
        ulong centerHigh, ulong centerLow,
        double radius,
        int precision = DefaultPrecision)
    {
        var (cx, cy, cz, cm) = Decode(centerHigh, centerLow, precision);
        uint r = (uint)Math.Ceiling(radius);
        ulong maxVal = (1UL << precision) - 1;

        uint minX = (uint)Math.Max(0, (long)cx - r);
        uint maxX = (uint)Math.Min(maxVal, (long)cx + r);
        uint minY = (uint)Math.Max(0, (long)cy - r);
        uint maxY = (uint)Math.Min(maxVal, (long)cy + r);
        uint minZ = (uint)Math.Max(0, (long)cz - r);
        uint maxZ = (uint)Math.Min(maxVal, (long)cz + r);
        uint minM = (uint)Math.Max(0, (long)cm - r);
        uint maxM = (uint)Math.Min(maxVal, (long)cm + r);

        var start = Encode(minX, minY, minZ, minM, precision);
        var end = Encode(maxX, maxY, maxZ, maxM, precision);
        
        if (start.High > end.High || (start.High == end.High && start.Low > end.Low))
        {
            return (end.High, end.Low, start.High, start.Low);
        }
        return (start.High, start.Low, end.High, end.Low);
    }
}