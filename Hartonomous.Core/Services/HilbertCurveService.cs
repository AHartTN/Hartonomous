using Hartonomous.Core.Native;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Core.Services;

/// <summary>
/// Provides conversion services between 4D spatial coordinates and 128-bit Hilbert curve indices.
/// The Hilbert curve is a space-filling curve that preserves locality, meaning nearby indices
/// correspond to nearby points in 4D space.
/// </summary>
/// <remarks>
/// Thread-safe. All methods delegate to the native library for computation.
/// The 128-bit Hilbert index provides sufficient precision for the entire Unicode codepoint space.
/// </remarks>
public static class HilbertCurveService
{
    /// <summary>
    /// Converts 4D coordinates to their corresponding Hilbert curve index.
    /// </summary>
    /// <param name="x">The X coordinate (0 to UInt32.MaxValue).</param>
    /// <param name="y">The Y coordinate (0 to UInt32.MaxValue).</param>
    /// <param name="z">The Z coordinate (0 to UInt32.MaxValue).</param>
    /// <param name="w">The W coordinate (0 to UInt32.MaxValue).</param>
    /// <returns>
    /// A <see cref="HilbertIndex128"/> representing the position on the Hilbert curve,
    /// or <c>null</c> if the conversion fails.
    /// </returns>
    public static HilbertIndex128? ConvertCoordinatesToIndex(uint x, uint y, uint z, uint w)
    {
        try
        {
            var result = NativeInterop.CoordsToHilbert(x, y, z, w, out var high, out var low);
            return result == 0 ? new HilbertIndex128(high, low) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a Hilbert curve index to its corresponding 4D coordinates.
    /// </summary>
    /// <param name="high">The high 64 bits of the 128-bit Hilbert index.</param>
    /// <param name="low">The low 64 bits of the 128-bit Hilbert index.</param>
    /// <returns>
    /// A <see cref="SpatialCoordinates4D"/> representing the 4D point,
    /// or <c>null</c> if the conversion fails.
    /// </returns>
    public static SpatialCoordinates4D? ConvertIndexToCoordinates(long high, long low)
    {
        try
        {
            var result = NativeInterop.HilbertToCoords(high, low, out var x, out var y, out var z, out var w);
            return result == 0 ? new SpatialCoordinates4D(x, y, z, w) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a Hilbert curve index to its corresponding 4D coordinates.
    /// </summary>
    /// <param name="index">The 128-bit Hilbert curve index.</param>
    /// <returns>
    /// A <see cref="SpatialCoordinates4D"/> representing the 4D point,
    /// or <c>null</c> if the conversion fails.
    /// </returns>
    public static SpatialCoordinates4D? ConvertIndexToCoordinates(HilbertIndex128 index) =>
        ConvertIndexToCoordinates(index.High, index.Low);
}
