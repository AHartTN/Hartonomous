namespace Hartonomous.CodeAtomizer.Core.Models;

/// <summary>
/// Result of atomizing source code, including all atoms, compositions, and relations
/// </summary>
public sealed class AtomizationResult
{
    public required Atom[] Atoms { get; init; }
    public required AtomComposition[] Compositions { get; init; }
    public required AtomRelation[] Relations { get; init; }
    public required int TotalAtoms { get; init; }
    public required int UniqueAtoms { get; init; }
}
