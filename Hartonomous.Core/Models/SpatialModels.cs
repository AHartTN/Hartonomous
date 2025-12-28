namespace Hartonomous.Core.Models;

/// <summary>
/// Result from a spatial query.
/// </summary>
public readonly record struct SpatialMatch(
    long HilbertHigh,
    long HilbertLow,
    int Codepoint,
    double Distance)
{
    /// <summary>
    /// The character this match represents (if valid Unicode scalar).
    /// </summary>
    public char? Character => Codepoint is >= 0 and <= 0xFFFF 
        ? (char)Codepoint 
        : null;

    /// <summary>
    /// The character as a string (handles surrogate pairs).
    /// </summary>
    public string CharacterString => char.ConvertFromUtf32(Codepoint);
}
