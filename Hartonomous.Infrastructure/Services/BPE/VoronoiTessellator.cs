using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace Hartonomous.Infrastructure.Services.BPE;

/// <summary>
/// Computes natural geometric neighbors using Voronoi Tessellation.
/// Projects 4D constants to 2D (X, Y) for planar tessellation using NetTopologySuite.
/// This provides a "natural neighborhood" graph for BPE vocabulary learning.
/// </summary>
public class VoronoiTessellator
{
    private readonly GeometryFactory _geometryFactory;

    public VoronoiTessellator()
    {
        _geometryFactory = new GeometryFactory();
    }

    public Task<List<ConstantPair>> ComputeNeighborsAsync(
        List<Constant> constants,
        CancellationToken cancellationToken = default)
    {
        if (constants == null || constants.Count < 2)
            return Task.FromResult(new List<ConstantPair>());

        // 1. Prepare sites for Voronoi
        // We project 4D (X, Y, Z, M) to 2D (X, Y) for NTS tessellation.
        // In our architecture, X is Spatial, Y is Entropy.
        // This clustering effectively groups items that are "spatially close" and "entropically similar".
        
        var sites = new List<Coordinate>(constants.Count);
        var constantMap = new Dictionary<Coordinate, Constant>();

        foreach (var constant in constants)
        {
            if (constant.Coordinate == null) continue;

            var coord = new Coordinate(constant.Coordinate.X, constant.Coordinate.Y);
            
            // Handle collisions (rare but possible with integer coords)
            // If multiple constants map to same X,Y, shift slightly or ignore?
            // We'll jitter slightly to ensure unique sites for tessellation.
            if (constantMap.ContainsKey(coord))
            {
                coord.X += 0.0001; 
                coord.Y += 0.0001;
            }

            sites.Add(coord);
            constantMap[coord] = constant;
        }

        if (sites.Count < 2)
            return Task.FromResult(new List<ConstantPair>());

        // 2. Build Voronoi Diagram
        var builder = new VoronoiDiagramBuilder();
        builder.SetSites(sites);
        
        // 3. Extract Adjacency Graph
        // NTS returns a GeometryCollection of Polygons.
        // Each Polygon's UserData is the generator Coordinate (site).
        var diagram = builder.GetDiagram(_geometryFactory);
        
        var polygons = new List<Polygon>();
        for (int i = 0; i < diagram.NumGeometries; i++)
        {
            if (diagram.GetGeometryN(i) is Polygon poly && poly.UserData is Coordinate site)
            {
                polygons.Add(poly);
            }
        }

        // 4. Find Neighbors (Adjacent Polygons)
        // Two polygons are neighbors if they intersect (share an edge)
        // Complexity: O(N^2) naive, but NTS spatial index makes it faster?
        // Better: Use Delaunay Triangulation which is the dual graph.
        // Delaunay edges directly represent Voronoi adjacency.
        
        var neighbors = ExtractNeighborsViaDelaunay(sites);
        
        // 5. Map back to ConstantPair
        var pairs = new List<ConstantPair>();
        var seenPairs = new HashSet<string>();

        foreach (var (siteA, siteB) in neighbors)
        {
            if (!constantMap.TryGetValue(siteA, out var constA) || 
                !constantMap.TryGetValue(siteB, out var constB))
                continue;

            // Ensure consistent ordering for unique pair ID
            var id1 = constA.Id;
            var id2 = constB.Id;
            if (id1.CompareTo(id2) > 0) (id1, id2) = (id2, id1);

            var pairKey = $"{id1}-{id2}";
            if (seenPairs.Contains(pairKey)) continue;
            seenPairs.Add(pairKey);

            // Calculate true 4D distance for the graph weight
            // (Voronoi found the topology, but physics uses 4D)
            double dist3D = constA.Coordinate!.DistanceTo(constB.Coordinate!);
            ulong hilbertDist = constA.Coordinate!.HilbertDistanceTo(constB.Coordinate!);

            pairs.Add(new ConstantPair
            {
                ConstantId1 = constA.Id,
                ConstantId2 = constB.Id,
                Distance3D = dist3D,
                HilbertDistance = hilbertDist
            });
        }

        return Task.FromResult(pairs);
    }

    private List<(Coordinate, Coordinate)> ExtractNeighborsViaDelaunay(List<Coordinate> sites)
    {
        var delaunayBuilder = new DelaunayTriangulationBuilder();
        delaunayBuilder.SetSites(sites);
        
        // Get edges of triangulation (these are the adjacency links)
        var edges = delaunayBuilder.GetEdges(_geometryFactory);
        var neighborPairs = new List<(Coordinate, Coordinate)>();

        for (int i = 0; i < edges.NumGeometries; i++)
        {
            if (edges.GetGeometryN(i) is LineString edge)
            {
                // A Delaunay edge connects two natural neighbors
                neighborPairs.Add((edge.Coordinates[0], edge.Coordinates[1]));
            }
        }

        return neighborPairs;
    }
}
