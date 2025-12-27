namespace Hartonomous.Core.Primitives;

/// <summary>
/// Identifies one of the eight cubic cells (3D boundary faces) of a tesseract.
/// Each face corresponds to a coordinate axis boundary in 4D space.
/// </summary>
/// <remarks>
/// <para>
/// A tesseract (4D hypercube) has 8 cubic cells as its 3D boundary, analogous to how
/// a cube has 6 square faces as its 2D boundary. Each cell is identified by which
/// axis it constrains and whether it's on the negative or positive boundary.
/// </para>
/// <para>
/// Center-origin geometry: Each face is located at coordinate = ±<see cref="int.MaxValue"/>.
/// </para>
/// </remarks>
public enum TesseractFace : byte
{
    /// <summary>The cell at X = -<see cref="int.MaxValue"/> (negative X boundary).</summary>
    XNegative = 0,
    
    /// <summary>The cell at X = +<see cref="int.MaxValue"/> (positive X boundary).</summary>
    XPositive = 1,
    
    /// <summary>The cell at Y = -<see cref="int.MaxValue"/> (negative Y boundary).</summary>
    YNegative = 2,
    
    /// <summary>The cell at Y = +<see cref="int.MaxValue"/> (positive Y boundary).</summary>
    YPositive = 3,
    
    /// <summary>The cell at Z = -<see cref="int.MaxValue"/> (negative Z boundary).</summary>
    ZNegative = 4,
    
    /// <summary>The cell at Z = +<see cref="int.MaxValue"/> (positive Z boundary).</summary>
    ZPositive = 5,
    
    /// <summary>The cell at W = -<see cref="int.MaxValue"/> (negative W boundary).</summary>
    WNegative = 6,
    
    /// <summary>The cell at W = +<see cref="int.MaxValue"/> (positive W boundary).</summary>
    WPositive = 7
}
