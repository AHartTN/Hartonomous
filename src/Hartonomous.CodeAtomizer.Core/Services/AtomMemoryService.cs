using Hartonomous.CodeAtomizer.Core.Models;
using System.Text.Json;

namespace Hartonomous.CodeAtomizer.Core.Services;

/// <summary>
/// In-memory atom storage and retrieval service for spatial proximity search.
/// Provides memory/context retrieval for code generation.
/// TODO: Replace with PostgreSQL + PostGIS for production.
/// </summary>
public class AtomMemoryService
{
    private readonly Dictionary<string, Atom> _atomsByHash = new();
    private readonly List<Atom> _atoms = new();
    private readonly Dictionary<string, List<AtomComposition>> _compositionsByParent = new();
    private readonly Dictionary<string, List<AtomComposition>> _compositionsByChild = new();
    private readonly Dictionary<string, List<AtomRelation>> _relationsBySource = new();
    private readonly Dictionary<string, List<AtomRelation>> _relationsByTarget = new();

    /// <summary>
    /// Store atomization results in memory
    /// </summary>
    public void Store(AtomizationResult result)
    {
        // Store atoms
        foreach (var atom in result.Atoms)
        {
            var hashKey = Convert.ToBase64String(atom.ContentHash);
            _atomsByHash[hashKey] = atom;
            _atoms.Add(atom);
        }

        // Store compositions with bidirectional lookup
        foreach (var composition in result.Compositions)
        {
            var parentKey = Convert.ToBase64String(composition.ParentAtomHash);
            var childKey = Convert.ToBase64String(composition.ComponentAtomHash);

            if (!_compositionsByParent.ContainsKey(parentKey))
                _compositionsByParent[parentKey] = new List<AtomComposition>();
            _compositionsByParent[parentKey].Add(composition);

            if (!_compositionsByChild.ContainsKey(childKey))
                _compositionsByChild[childKey] = new List<AtomComposition>();
            _compositionsByChild[childKey].Add(composition);
        }

        // Store relations with bidirectional lookup
        foreach (var relation in result.Relations)
        {
            var sourceKey = Convert.ToBase64String(relation.SourceAtomHash);
            var targetKey = Convert.ToBase64String(relation.TargetAtomHash);

            if (!_relationsBySource.ContainsKey(sourceKey))
                _relationsBySource[sourceKey] = new List<AtomRelation>();
            _relationsBySource[sourceKey].Add(relation);

            if (!_relationsByTarget.ContainsKey(targetKey))
                _relationsByTarget[targetKey] = new List<AtomRelation>();
            _relationsByTarget[targetKey].Add(relation);
        }
    }

    /// <summary>
    /// Retrieve atoms within spatial proximity (Euclidean distance)
    /// </summary>
    public List<Atom> RetrieveByProximity(double x, double y, double z, double radius, int maxResults = 50)
    {
        var results = new List<(Atom atom, double distance)>();

        foreach (var atom in _atoms)
        {
            var dx = atom.SpatialKey.X - x;
            var dy = atom.SpatialKey.Y - y;
            var dz = atom.SpatialKey.Z - z;
            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            if (distance <= radius)
            {
                results.Add((atom, distance));
            }
        }

        return results
            .OrderBy(r => r.distance)
            .Take(maxResults)
            .Select(r => r.atom)
            .ToList();
    }

    /// <summary>
    /// Retrieve atoms by Hilbert index range (efficient range queries)
    /// </summary>
    public List<Atom> RetrieveByHilbertRange(long startIndex, long endIndex, int maxResults = 50)
    {
        return _atoms
            .Where(a => a.HilbertIndex >= startIndex && a.HilbertIndex <= endIndex)
            .OrderBy(a => a.HilbertIndex)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Retrieve atoms by semantic category and modality
    /// </summary>
    public List<Atom> RetrieveBySemantic(string? category = null, string? modality = null, int maxResults = 100)
    {
        var query = _atoms.AsEnumerable();

        if (!string.IsNullOrEmpty(category))
            query = query.Where(a => a.Subtype.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(modality))
            query = query.Where(a => a.Modality.Equals(modality, StringComparison.OrdinalIgnoreCase));

        return query.Take(maxResults).ToList();
    }

    /// <summary>
    /// Search atoms by text content (full-text search simulation)
    /// </summary>
    public List<Atom> SearchByText(string searchText, int maxResults = 20)
    {
        return _atoms
            .Where(a => 
                (a.CanonicalText?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.Metadata?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Reconstruct composition tree (parent with all descendants)
    /// </summary>
    public CompositionTree ReconstructCompositionTree(byte[] rootAtomHash, int maxDepth = 10)
    {
        var hashKey = Convert.ToBase64String(rootAtomHash);
        var rootAtom = _atomsByHash.GetValueOrDefault(hashKey);

        if (rootAtom == null)
            return new CompositionTree { Root = null, Children = new List<CompositionTree>() };

        var tree = new CompositionTree { Root = rootAtom, Children = new List<CompositionTree>() };

        if (maxDepth > 0 && _compositionsByParent.TryGetValue(hashKey, out var childCompositions))
        {
            foreach (var composition in childCompositions.OrderBy(c => c.SequenceIndex))
            {
                var childTree = ReconstructCompositionTree(composition.ComponentAtomHash, maxDepth - 1);
                if (childTree.Root != null)
                {
                    tree.Children.Add(childTree);
                }
            }
        }

        return tree;
    }

    /// <summary>
    /// Get all relations for an atom (both incoming and outgoing)
    /// </summary>
    public AtomRelationSet GetRelations(byte[] atomHash)
    {
        var hashKey = Convert.ToBase64String(atomHash);

        var outgoing = _relationsBySource.GetValueOrDefault(hashKey) ?? new List<AtomRelation>();
        var incoming = _relationsByTarget.GetValueOrDefault(hashKey) ?? new List<AtomRelation>();

        return new AtomRelationSet
        {
            OutgoingRelations = outgoing,
            IncomingRelations = incoming
        };
    }

    /// <summary>
    /// Build context for code generation by gathering related atoms
    /// </summary>
    public GenerationContext BuildContext(
        string language,
        string category,
        double x, double y, double z,
        double proximityRadius = 0.2,
        int maxAtoms = 30)
    {
        // 1. Get spatially nearby atoms
        var nearbyAtoms = RetrieveByProximity(x, y, z, proximityRadius, maxAtoms / 2);

        // 2. Get semantically similar atoms (same category)
        var semanticAtoms = RetrieveBySemantic(category, "code", maxAtoms / 2);

        // 3. Combine and deduplicate
        var contextAtoms = nearbyAtoms
            .Concat(semanticAtoms)
            .DistinctBy(a => Convert.ToBase64String(a.ContentHash))
            .Take(maxAtoms)
            .ToList();

        // 4. Gather relations
        var relations = new List<AtomRelation>();
        foreach (var atom in contextAtoms)
        {
            var atomRelations = GetRelations(atom.ContentHash);
            relations.AddRange(atomRelations.OutgoingRelations);
        }

        return new GenerationContext
        {
            Language = language,
            Category = category,
            FocalPoint = new SpatialPosition(x, y, z),
            ContextAtoms = contextAtoms,
            Relations = relations.DistinctBy(r => 
                $"{Convert.ToBase64String(r.SourceAtomHash)}:{Convert.ToBase64String(r.TargetAtomHash)}").ToList()
        };
    }

    /// <summary>
    /// Get statistics about stored atoms
    /// </summary>
    public MemoryStatistics GetStatistics()
    {
        var modalityCounts = _atoms
            .GroupBy(a => a.Modality)
            .ToDictionary(g => g.Key, g => g.Count());

        var categoryCounts = _atoms
            .GroupBy(a => a.Subtype)
            .ToDictionary(g => g.Key, g => g.Count());

        return new MemoryStatistics
        {
            TotalAtoms = _atoms.Count,
            TotalCompositions = _compositionsByParent.Values.Sum(l => l.Count),
            TotalRelations = _relationsBySource.Values.Sum(l => l.Count),
            ModalityCounts = modalityCounts,
            CategoryCounts = categoryCounts
        };
    }

    /// <summary>
    /// Clear all stored data
    /// </summary>
    public void Clear()
    {
        _atomsByHash.Clear();
        _atoms.Clear();
        _compositionsByParent.Clear();
        _compositionsByChild.Clear();
        _relationsBySource.Clear();
        _relationsByTarget.Clear();
    }
}

/// <summary>
/// Composition tree structure for reconstructing hierarchies
/// </summary>
public class CompositionTree
{
    public Atom? Root { get; set; }
    public List<CompositionTree> Children { get; set; } = new();
}

/// <summary>
/// Atom relation set (incoming + outgoing)
/// </summary>
public class AtomRelationSet
{
    public List<AtomRelation> OutgoingRelations { get; set; } = new();
    public List<AtomRelation> IncomingRelations { get; set; } = new();
}

/// <summary>
/// Context for code generation
/// </summary>
public class GenerationContext
{
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public SpatialPosition FocalPoint { get; set; } = new(0, 0, 0);
    public List<Atom> ContextAtoms { get; set; } = new();
    public List<AtomRelation> Relations { get; set; } = new();
}

/// <summary>
/// Memory statistics
/// </summary>
public class MemoryStatistics
{
    public int TotalAtoms { get; set; }
    public int TotalCompositions { get; set; }
    public int TotalRelations { get; set; }
    public Dictionary<string, int> ModalityCounts { get; set; } = new();
    public Dictionary<string, int> CategoryCounts { get; set; } = new();
}
