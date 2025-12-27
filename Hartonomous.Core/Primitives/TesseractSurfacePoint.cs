namespace Hartonomous.Core.Primitives;

/// <summary>
/// Represents a point on the exterior surface of a 4-dimensional hypercube (tesseract).
/// Uses center-origin geometry where the origin is at (0,0,0,0) and coordinates are signed integers.
/// </summary>
/// <remarks>
/// <para>
/// A tesseract is the 4D analog of a cube. Its surface consists of 8 cubic cells,
/// each identified by a <see cref="TesseractFace"/> value. Points on the surface
/// always have exactly one coordinate at the boundary value (±<see cref="Boundary"/>).
/// </para>
/// <para>
/// This value type is computed by the native Hartonomous library and represents
/// the spatial projection of a Unicode codepoint onto the tesseract surface.
/// </para>
/// </remarks>
/// <param name="X">The X coordinate in signed 32-bit space.</param>
/// <param name="Y">The Y coordinate in signed 32-bit space.</param>
/// <param name="Z">The Z coordinate in signed 32-bit space.</param>
/// <param name="W">The W coordinate (fourth dimension) in signed 32-bit space.</param>
/// <param name="Face">The tesseract face on which this point lies.</param>
public readonly record struct TesseractSurfacePoint(
    int X, int Y, int Z, int W, TesseractFace Face)
{
    /// <summary>
    /// The absolute boundary value for tesseract surface coordinates.
    /// Surface faces are located at ±<see cref="Boundary"/> along each axis.
    /// </summary>
    public const int Boundary = int.MaxValue;

    /// <summary>
    /// Gets the Euclidean distance from this point to the origin (0,0,0,0).
    /// </summary>
    /// <value>The distance as a double-precision floating-point number.</value>
    public double DistanceFromOrigin =>
        Math.Sqrt((double)X * X + (double)Y * Y + (double)Z * Z + (double)W * W);
}
