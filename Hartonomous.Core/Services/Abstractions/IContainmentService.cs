using Hartonomous.Core.Models;

namespace Hartonomous.Core.Services.Abstractions;

/// <summary>
/// Containment queries - find substrings and compositions.
/// Uses content-defined chunking for consistent boundaries.
/// </summary>
public interface IContainmentService
{
    /// <summary>
    /// Check if substring exists in any stored content.
    /// "Captain Ahab" produces the same chunks whether standalone or in Moby Dick.
    /// </summary>
    bool ContainsSubstring(string text);

    /// <summary>
    /// Find compositions containing a substring.
    /// Returns roots of compositions that contain the substring.
    /// Uses recursive CTE to walk up the tree.
    /// </summary>
    NodeId[] FindContaining(string text, int limit = 100);
}
