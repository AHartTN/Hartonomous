using System.Text;
using Hartonomous.Core.Native;

namespace Hartonomous.Core.Services;

/// <summary>
/// Spatial queries using PostGIS-backed semantic proximity.
/// Find similar characters, case variants, diacriticals via geometry.
/// </summary>
public sealed class SpatialQueryService
{
    private readonly DatabaseService _db;

    public SpatialQueryService(DatabaseService? db = null)
    {
        _db = db ?? DatabaseService.Instance;
    }

    /// <summary>
    /// Find atoms semantically similar to a codepoint using KNN.
    /// Uses the 4D semantic position geometry for proximity.
    /// </summary>
    public SpatialMatch[] FindSimilar(int codepoint, int limit = 20)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeSpatialMatch[limit];
        var status = NativeInterop.FindSimilar(codepoint, results, limit, out var count);
        
        if (status < 0)
            throw new InvalidOperationException($"FindSimilar failed: error {status}");

        return ConvertMatches(results, count);
    }

    /// <summary>
    /// Find atoms within distance of a codepoint's semantic position.
    /// Uses ST_DWithin with GIST index.
    /// </summary>
    public SpatialMatch[] FindNear(int codepoint, double distanceThreshold, int limit = 100)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeSpatialMatch[limit];
        var status = NativeInterop.FindNear(codepoint, distanceThreshold, results, limit, out var count);
        
        if (status < 0)
            throw new InvalidOperationException($"FindNear failed: error {status}");

        return ConvertMatches(results, count);
    }

    /// <summary>
    /// Find all case variants of a character (same base, different variant).
    /// 'a' finds 'A', 'à', 'á', 'À', etc. - automatic, no manual linking.
    /// </summary>
    public SpatialMatch[] FindCaseVariants(int codepoint, int limit = 50)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeSpatialMatch[limit];
        var status = NativeInterop.FindCaseVariants(codepoint, results, limit, out var count);
        
        if (status < 0)
            throw new InvalidOperationException($"FindCaseVariants failed: error {status}");

        return ConvertMatches(results, count);
    }

    /// <summary>
    /// Find all case variants for a character.
    /// </summary>
    public SpatialMatch[] FindCaseVariants(char c, int limit = 50) 
        => FindCaseVariants((int)c, limit);

    /// <summary>
    /// Find all diacritical variants of a base character.
    /// 'e' finds 'é', 'è', 'ê', 'ë', etc.
    /// </summary>
    public SpatialMatch[] FindDiacriticalVariants(int codepoint, int limit = 50)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeSpatialMatch[limit];
        var status = NativeInterop.FindDiacriticalVariants(codepoint, results, limit, out var count);
        
        if (status < 0)
            throw new InvalidOperationException($"FindDiacriticalVariants failed: error {status}");

        return ConvertMatches(results, count);
    }

    /// <summary>
    /// Find all diacritical variants for a character.
    /// </summary>
    public SpatialMatch[] FindDiacriticalVariants(char c, int limit = 50) 
        => FindDiacriticalVariants((int)c, limit);

    private static SpatialMatch[] ConvertMatches(NativeInterop.NativeSpatialMatch[] native, int count)
    {
        var result = new SpatialMatch[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new SpatialMatch(
                native[i].HilbertHigh,
                native[i].HilbertLow,
                native[i].Codepoint,
                native[i].Distance);
        }
        return result;
    }
}

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
