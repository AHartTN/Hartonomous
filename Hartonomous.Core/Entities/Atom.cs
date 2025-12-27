using Hartonomous.Core.Native;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Core.Entities;

/// <summary>
/// The fundamental atomic unit of the system. Each Atom represents a single Unicode codepoint
/// projected onto the surface of a 4D hypercube (tesseract) and indexed via a 128-bit Hilbert curve.
/// 
/// All heavy computation is performed by the native C++ library.
/// </summary>
public sealed class Atom : IEquatable<Atom>
{
    public int Codepoint { get; }
    public TesseractSurfacePoint SurfacePoint { get; }
    public HilbertIndex128 HilbertIndex { get; }

    private Atom(int codepoint, TesseractSurfacePoint surfacePoint, HilbertIndex128 hilbertIndex)
    {
        Codepoint = codepoint;
        SurfacePoint = surfacePoint;
        HilbertIndex = hilbertIndex;
    }

    /// <summary>
    /// Create an Atom for a Unicode codepoint. Calls into native library for computation.
    /// </summary>
    public static Atom Create(int codepoint)
    {
        var result = NativeInterop.MapCodepoint(codepoint, out var native);
        if (result != 0)
            throw new ArgumentOutOfRangeException(nameof(codepoint), $"Invalid codepoint: U+{codepoint:X4}");

        var surfacePoint = new TesseractSurfacePoint(
            native.X, native.Y, native.Z, native.W,
            (TesseractFace)native.Face);

        var hilbertIndex = new HilbertIndex128(native.HilbertHigh, native.HilbertLow);

        return new Atom(codepoint, surfacePoint, hilbertIndex);
    }

    /// <summary>
    /// Create atoms for a range of codepoints efficiently (batched native call with parallel processing).
    /// </summary>
    public static IReadOnlyList<Atom> CreateRange(int start, int end)
    {
        var capacity = end - start + 1;
        var nativeAtoms = new NativeAtom[capacity];
        var count = NativeInterop.MapCodepointRange(start, end, nativeAtoms, capacity);

        // Pre-allocate array and process in parallel for large ranges
        var atoms = new Atom[count];
        
        if (count > 64)
        {
            Parallel.For(0, count, i =>
            {
                ref var native = ref nativeAtoms[i];
                var surfacePoint = new TesseractSurfacePoint(
                    native.X, native.Y, native.Z, native.W,
                    (TesseractFace)native.Face);
                var hilbertIndex = new HilbertIndex128(native.HilbertHigh, native.HilbertLow);
                atoms[i] = new Atom(native.Codepoint, surfacePoint, hilbertIndex);
            });
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                ref var native = ref nativeAtoms[i];
                var surfacePoint = new TesseractSurfacePoint(
                    native.X, native.Y, native.Z, native.W,
                    (TesseractFace)native.Face);
                var hilbertIndex = new HilbertIndex128(native.HilbertHigh, native.HilbertLow);
                atoms[i] = new Atom(native.Codepoint, surfacePoint, hilbertIndex);
            }
        }

        return atoms;
    }

    /// <summary>
    /// Get the character representation if this is a displayable codepoint.
    /// </summary>
    public string? Character => char.IsSurrogate((char)Codepoint) || Codepoint > 0xFFFF
        ? (Codepoint <= 0x10FFFF ? char.ConvertFromUtf32(Codepoint) : null)
        : new string((char)Codepoint, 1);

    public bool Equals(Atom? other) => other is not null && Codepoint == other.Codepoint;
    public override bool Equals(object? obj) => Equals(obj as Atom);
    public override int GetHashCode() => Codepoint;
    public override string ToString() => $"U+{Codepoint:X4} '{Character ?? "N/A"}' @ {HilbertIndex}";
}
