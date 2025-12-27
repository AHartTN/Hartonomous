namespace Hartonomous.Core.Primitives;

/// <summary>
/// Represents a 128-bit Hilbert curve index as two 64-bit components.
/// This value type provides a unique, locality-preserving identifier for points in 4D space.
/// </summary>
/// <remarks>
/// <para>
/// The Hilbert curve is a space-filling curve that maps 4D coordinates to a single 128-bit index
/// while preserving spatial locality. This means nearby indices generally correspond to nearby points.
/// </para>
/// <para>
/// This structure is computed by the native Hartonomous library and should be treated as immutable.
/// The index is stored as two 64-bit signed integers representing the high and low portions.
/// </para>
/// </remarks>
/// <param name="High">The high-order 64 bits of the 128-bit Hilbert index.</param>
/// <param name="Low">The low-order 64 bits of the 128-bit Hilbert index.</param>
public readonly record struct HilbertIndex128(long High, long Low) : IComparable<HilbertIndex128>
{
    public int CompareTo(HilbertIndex128 other)
    {
        // Compare as unsigned 128-bit integers
        var thisHigh = unchecked((ulong)High);
        var thisLow = unchecked((ulong)Low);
        var otherHigh = unchecked((ulong)other.High);
        var otherLow = unchecked((ulong)other.Low);

        var highCompare = thisHigh.CompareTo(otherHigh);
        return highCompare != 0 ? highCompare : thisLow.CompareTo(otherLow);
    }

    public static bool operator <(HilbertIndex128 left, HilbertIndex128 right) => left.CompareTo(right) < 0;
    public static bool operator >(HilbertIndex128 left, HilbertIndex128 right) => left.CompareTo(right) > 0;
    public static bool operator <=(HilbertIndex128 left, HilbertIndex128 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(HilbertIndex128 left, HilbertIndex128 right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"0x{unchecked((ulong)High):X16}{unchecked((ulong)Low):X16}";
}
