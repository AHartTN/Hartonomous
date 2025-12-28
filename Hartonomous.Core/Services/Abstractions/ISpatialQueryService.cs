using Hartonomous.Core.Models;

namespace Hartonomous.Core.Services.Abstractions;

/// <summary>
/// Spatial queries using semantic proximity.
/// Find similar characters, case variants, diacriticals via geometry.
/// </summary>
public interface ISpatialQueryService
{
    /// <summary>
    /// Find atoms semantically similar to a codepoint using KNN.
    /// Uses the 4D semantic position geometry for proximity.
    /// </summary>
    SpatialMatch[] FindSimilar(int codepoint, int limit = 20);

    /// <summary>
    /// Find atoms within distance of a codepoint's semantic position.
    /// </summary>
    SpatialMatch[] FindNear(int codepoint, double distanceThreshold, int limit = 100);

    /// <summary>
    /// Find all case variants of a character (same base, different variant).
    /// 'a' finds 'A', 'à', 'á', 'À', etc. - automatic, no manual linking.
    /// </summary>
    SpatialMatch[] FindCaseVariants(int codepoint, int limit = 50);

    /// <summary>
    /// Find all case variants for a character.
    /// </summary>
    SpatialMatch[] FindCaseVariants(char c, int limit = 50);

    /// <summary>
    /// Find all diacritical variants of a base character.
    /// 'e' finds 'é', 'è', 'ê', 'ë', etc.
    /// </summary>
    SpatialMatch[] FindDiacriticalVariants(int codepoint, int limit = 50);

    /// <summary>
    /// Find all diacritical variants for a character.
    /// </summary>
    SpatialMatch[] FindDiacriticalVariants(char c, int limit = 50);
}
