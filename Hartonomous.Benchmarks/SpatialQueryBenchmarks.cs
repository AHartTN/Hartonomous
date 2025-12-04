using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;
using Hartonomous.Data.Extensions;
using Hartonomous.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Benchmarks;

/// <summary>
/// Benchmarks comparing PostGIS R-tree vs Hilbert B-tree spatial queries
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SpatialQueryBenchmarks
{
    private ApplicationDbContext _context = null!;
    private ConstantRepository _repository = null!;
    private SpatialCoordinate _queryCenter = null!;
    private const int K = 10;
    private const double Radius = 1000.0;

    [GlobalSetup]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=5432;Database=hartonomous;Username=hartonomous;Password=Revolutionary-AI-2025!Geometry",
                o => o.UseNetTopologySuite())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ConstantRepository(_context);
        
        // Query center in the middle of the coordinate space
        _queryCenter = SpatialCoordinate.Create(500.0, 500.0, 500.0, precision: 21);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
    }

    [Benchmark(Description = "Hilbert B-tree k-NN query (k=10)")]
    public async Task<int> HilbertKNearestQuery()
    {
        var results = await _repository.GetKNearestConstantsAsync(_queryCenter, K);
        return results.Count();
    }

    [Benchmark(Description = "Hilbert B-tree proximity query (radius=1000)")]
    public async Task<int> HilbertProximityQuery()
    {
        var results = await _repository.GetNearbyConstantsAsync(_queryCenter, Radius, maxResults: 100);
        return results.Count();
    }

    [Benchmark(Description = "Hilbert B-tree range query")]
    public async Task<int> HilbertRangeQuery()
    {
        var centerIndex = _queryCenter.HilbertIndex;
        var rangeSize = 1000UL;
        var results = await _repository.GetByHilbertRangeAsync(
            centerIndex - rangeSize,
            centerIndex + rangeSize,
            maxResults: 100);
        return results.Count();
    }

    [Benchmark(Baseline = true, Description = "PostGIS R-tree k-NN query (k=10) - LEGACY")]
    public async Task<int> PostGisKNearestQuery()
    {
        // Simulate old PostGIS approach for comparison
        var point = new NetTopologySuite.Geometries.Point(_queryCenter.X, _queryCenter.Y, _queryCenter.Z);
        var results = await _context.Set<Constant>()
            .Where(c => c.Location != null && c.Status == ConstantStatus.Active)
            .OrderBy(c => c.Location!.Distance(point))
            .Take(K)
            .ToListAsync();
        return results.Count;
    }

    [Benchmark(Description = "PostGIS R-tree proximity query (radius=1000) - LEGACY")]
    public async Task<int> PostGisProximityQuery()
    {
        // Simulate old PostGIS approach for comparison
        var point = new NetTopologySuite.Geometries.Point(_queryCenter.X, _queryCenter.Y, _queryCenter.Z);
        var results = await _context.Set<Constant>()
            .Where(c => c.Location != null && c.Status == ConstantStatus.Active)
            .Where(c => c.Location!.Distance(point) <= Radius)
            .OrderBy(c => c.Location!.Distance(point))
            .Take(100)
            .ToListAsync();
        return results.Count;
    }
}

/// <summary>
/// Program entry point for running benchmarks
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<SpatialQueryBenchmarks>();
    }
}
