using System.Text;
using Hartonomous.Core.Models;
using Hartonomous.Core.Native;
using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Core.Services;

/// <summary>
/// Containment queries - find substrings and compositions.
/// Uses content-defined chunking for consistent boundaries.
/// </summary>
public sealed class ContainmentService : IContainmentService
{
    private readonly IDatabaseService _db;

    public ContainmentService(IDatabaseService? db = null)
    {
        _db = db ?? DatabaseService.Instance;
    }

    /// <summary>
    /// Check if substring exists in any stored content.
    /// "Captain Ahab" produces the same chunks whether standalone or in Moby Dick.
    /// </summary>
    public bool ContainsSubstring(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return true;

        _db.Initialize();

        var bytes = Encoding.UTF8.GetBytes(text);
        var status = NativeInterop.ContainsSubstring(bytes, bytes.Length, out var exists);

        if (status < 0)
            throw new InvalidOperationException($"ContainsSubstring failed: error {status}");

        return exists != 0;
    }

    /// <summary>
    /// Find compositions containing a substring.
    /// Returns roots of compositions that contain the substring.
    /// Uses recursive CTE to walk up the tree.
    /// </summary>
    public NodeId[] FindContaining(string text, int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return [];

        _db.Initialize();

        var bytes = Encoding.UTF8.GetBytes(text);
        var results = new long[limit * 2]; // Pairs of high/low
        
        var status = NativeInterop.FindContaining(bytes, bytes.Length, results, limit, out var count);

        if (status < 0)
            throw new InvalidOperationException($"FindContaining failed: error {status}");

        var nodes = new NodeId[count];
        for (int i = 0; i < count; i++)
        {
            nodes[i] = new NodeId(results[i * 2], results[i * 2 + 1]);
        }
        return nodes;
    }
}
