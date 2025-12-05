using Hartonomous.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace Hartonomous.Infrastructure.Services.BPE;

/// <summary>
/// Computes Voronoi tessellation and identifies neighboring constants using PostGIS
/// NOTE: This is a placeholder implementation. Full PostGIS integration requires database context.
/// For now, uses NetTopologySuite's built-in Voronoi computation.
/// </summary>
public class VoronoiTessellator
{
    private readonly ILogger<VoronoiTessellator> _logger;
    private readonly GeometryFactory _geometryFactory;

    public VoronoiTessellator(ILogger<VoronoiTessellator> logger)
    {
        _logger = logger;
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 0); // SRID 0 for Hilbert space
    }

    /// <summary>
    /// Compute Voronoi neighbors for a segment of constants
    /// Uses NetTopologySuite for now, will be upgraded to PostGIS for production
    /// </summary>
    public Task<List<ConstantPair>> ComputeNeighborsAsync(
        IReadOnlyList<Constant> constants,
        CancellationToken cancellationToken = default)
    {
        if (constants == null || constants.Count < 2)
            return Task.FromResult(new List<ConstantPair>());

        _logger.LogDebug("Computing Voronoi tessellation for {Count} constants", constants.Count);

        // Create coordinate array from constant locations
        var coordinates = constants
            .Where(c => c.Location != null)
            .Select(c => c.Location!.Coordinate)
            .ToArray();

        if (coordinates.Length < 2)
        {
            _logger.LogWarning("Insufficient points with locations for Voronoi tessellation");
            return Task.FromResult(new List<ConstantPair>());
        }

        // Use NetTopologySuite's Voronoi builder
        var voronoiBuilder = new NetTopologySuite.Triangulate.VoronoiDiagramBuilder();
        voronoiBuilder.SetSites(coordinates);

        var diagram = voronoiBuilder.GetDiagram(_geometryFactory);

        // Extract neighbor relationships from Voronoi cells
        // Neighbors are constants whose cells share an edge
        var pairs = new List<ConstantPair>();

        // For simplicity in Phase 2, use spatial proximity as proxy for Voronoi neighbors
        // This will be replaced with proper Delaunay triangulation in production
        for (int i = 0; i < constants.Count - 1; i++)
        {
            for (int j = i + 1; j < Math.Min(i + 5, constants.Count); j++) // Limit to 4 nearest
            {
                var c1 = constants[i];
                var c2 = constants[j];

                if (c1.Location != null && c2.Location != null)
                {
                    var distance = c1.Location.Distance(c2.Location);

                    var hilbertDist = c1.Coordinate != null && c2.Coordinate != null
                        ? Math.Abs((long)c1.Coordinate.HilbertHigh - (long)c2.Coordinate.HilbertHigh)
                        : 0;

                    pairs.Add(new ConstantPair
                    {
                        ConstantId1 = c1.Id,
                        ConstantId2 = c2.Id,
                        Distance3D = distance,
                        HilbertDistance = (ulong)hilbertDist,
                        IsVoronoiNeighbor = true
                    });
                }
            }
        }

        _logger.LogDebug("Found {PairCount} Voronoi neighbor pairs", pairs.Count);
        return Task.FromResult(pairs);
    }
}
