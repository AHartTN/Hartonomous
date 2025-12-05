using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Services.BPE;

/// <summary>
/// Graph representation for MST computation
/// </summary>
public class Graph
{
    public List<Edge> Edges { get; } = new();
    public HashSet<Guid> Vertices { get; } = new();

    public void AddEdge(Guid vertex1, Guid vertex2, double weight, ulong hilbertDistance)
    {
        Edges.Add(new Edge
        {
            Vertex1 = vertex1,
            Vertex2 = vertex2,
            Weight = weight,
            HilbertDistance = hilbertDistance
        });
        Vertices.Add(vertex1);
        Vertices.Add(vertex2);
    }
}

/// <summary>
/// Edge in the graph with weight
/// </summary>
public record Edge
{
    public required Guid Vertex1 { get; init; }
    public required Guid Vertex2 { get; init; }
    public required double Weight { get; init; }
    public required ulong HilbertDistance { get; init; }
}

/// <summary>
/// Computes Minimum Spanning Tree using Kruskal's algorithm
/// </summary>
public class MinimumSpanningTreeComputer
{
    private readonly ILogger<MinimumSpanningTreeComputer> _logger;

    public MinimumSpanningTreeComputer(ILogger<MinimumSpanningTreeComputer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Compute MST from a graph using Kruskal's algorithm with Union-Find
    /// </summary>
    public Graph ComputeMST(Graph graph)
    {
        if (graph.Edges.Count == 0)
            return new Graph();

        _logger.LogDebug("Computing MST for graph with {EdgeCount} edges, {VertexCount} vertices",
            graph.Edges.Count, graph.Vertices.Count);

        // Sort edges by weight (ascending)
        var sortedEdges = graph.Edges.OrderBy(e => e.Weight).ToList();

        var mst = new Graph();
        var unionFind = new UnionFind(graph.Vertices);

        foreach (var edge in sortedEdges)
        {
            // Check if adding this edge creates a cycle
            if (!unionFind.AreConnected(edge.Vertex1, edge.Vertex2))
            {
                // Add edge to MST
                mst.AddEdge(edge.Vertex1, edge.Vertex2, edge.Weight, edge.HilbertDistance);
                unionFind.Union(edge.Vertex1, edge.Vertex2);

                // MST is complete when we have V-1 edges
                if (mst.Edges.Count == graph.Vertices.Count - 1)
                    break;
            }
        }

        _logger.LogDebug("MST computed with {EdgeCount} edges", mst.Edges.Count);
        return mst;
    }

    /// <summary>
    /// Union-Find (Disjoint Set) data structure for cycle detection
    /// </summary>
    private class UnionFind
    {
        private readonly Dictionary<Guid, Guid> _parent = new();
        private readonly Dictionary<Guid, int> _rank = new();

        public UnionFind(IEnumerable<Guid> vertices)
        {
            foreach (var vertex in vertices)
            {
                _parent[vertex] = vertex;
                _rank[vertex] = 0;
            }
        }

        public Guid Find(Guid vertex)
        {
            if (!_parent[vertex].Equals(vertex))
            {
                // Path compression
                _parent[vertex] = Find(_parent[vertex]);
            }
            return _parent[vertex];
        }

        public void Union(Guid vertex1, Guid vertex2)
        {
            var root1 = Find(vertex1);
            var root2 = Find(vertex2);

            if (root1.Equals(root2))
                return;

            // Union by rank
            if (_rank[root1] < _rank[root2])
            {
                _parent[root1] = root2;
            }
            else if (_rank[root1] > _rank[root2])
            {
                _parent[root2] = root1;
            }
            else
            {
                _parent[root2] = root1;
                _rank[root1]++;
            }
        }

        public bool AreConnected(Guid vertex1, Guid vertex2)
        {
            return Find(vertex1).Equals(Find(vertex2));
        }
    }
}
