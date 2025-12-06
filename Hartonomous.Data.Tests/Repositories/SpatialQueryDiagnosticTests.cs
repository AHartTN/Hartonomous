using FluentAssertions;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Repositories;
using Hartonomous.Data.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Hartonomous.Data.Tests.Repositories;

/// <summary>
/// Diagnostic tests to understand Hilbert distance calculation behavior.
/// Used to debug spatial query issues.
/// </summary>
[Collection("Sequential")]
public class SpatialQueryDiagnosticTests : IClassFixture<InMemoryDatabaseFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly ConstantRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public SpatialQueryDiagnosticTests(InMemoryDatabaseFixture fixture, ITestOutputHelper output)
    {
        _output = output;
        var scope = fixture.ServiceProvider.CreateScope();
        _repository = new ConstantRepository(fixture.Context);
        _unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
    }

    [Fact]
    public async Task Diagnostic_InvestigateHilbertDistanceCalculation()
    {
        // Arrange - Create coordinates identical to failing test
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        var nearby = SpatialCoordinate.FromUniversalProperties(0, 510000, 760000, 150);

        _output.WriteLine("=== COORDINATES ===");
        _output.WriteLine($"Center: X={center.X}, Y={center.Y}, Z={center.Z}, M={center.M}");
        _output.WriteLine($"Center Hilbert: High={center.HilbertHigh:X}, Low={center.HilbertLow:X}");
        _output.WriteLine($"Nearby: X={nearby.X}, Y={nearby.Y}, Z={nearby.Z}, M={nearby.M}");
        _output.WriteLine($"Nearby Hilbert: High={nearby.HilbertHigh:X}, Low={nearby.HilbertLow:X}");
        
        // Calculate Euclidean distance
        var euclideanDist = center.DistanceTo(nearby);
        _output.WriteLine($"\nEuclidean Distance: {euclideanDist:F2}");
        
        // Calculate Hilbert distance
        var hilbertDist = nearby.HilbertDistanceTo(center);
        _output.WriteLine($"Hilbert Distance: {hilbertDist}");
        _output.WriteLine($"Hilbert Distance (decimal): {hilbertDist}");
        
        // Create and save constant at nearby location
        var active = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        active.SetCoordinateForTesting(nearby);
        active.ActivateForTesting();
        
        await _repository.AddAsync(active);
        await _unitOfWork.SaveChangesAsync();
        
        _output.WriteLine("\n=== SAVED CONSTANT ===");
        _output.WriteLine($"Active ID: {active.Id}");
        _output.WriteLine($"Active Status: {active.Status}");
        _output.WriteLine($"Active Coordinate: {active.Coordinate}");
        _output.WriteLine($"Active Coordinate != null: {active.Coordinate != null}");
        
        if (active.Coordinate != null)
        {
            _output.WriteLine($"Active Coord X={active.Coordinate.X}, Y={active.Coordinate.Y}, Z={active.Coordinate.Z}, M={active.Coordinate.M}");
            _output.WriteLine($"Active Hilbert: High={active.Coordinate.HilbertHigh:X}, Low={active.Coordinate.HilbertLow:X}");
            
            var distFromCenter = active.Coordinate.HilbertDistanceTo(center);
            _output.WriteLine($"Distance from Center: {distFromCenter}");
        }
        
        // Query with various radius values
        var testRadii = new[] { 100000.0, 1000000.0, 10000000.0, 100000000.0, 1000000000.0, 10000000000.0 };
        
        _output.WriteLine("\n=== QUERY RESULTS ===");
        foreach (var radius in testRadii)
        {
            var threshold = (ulong)(radius * 1000);
            var results = await _repository.GetNearbyConstantsAsync(center, radius, maxResults: 10);
            var resultsList = results.ToList();
            
            _output.WriteLine($"Radius: {radius}, Threshold: {threshold}");
            _output.WriteLine($"  Results Count: {resultsList.Count}");
            _output.WriteLine($"  Contains Active: {resultsList.Any(c => c.Id == active.Id)}");
            
            if (resultsList.Any())
            {
                foreach (var result in resultsList)
                {
                    if (result.Coordinate != null)
                    {
                        var dist = result.Coordinate.HilbertDistanceTo(center);
                        _output.WriteLine($"    Found: {result.Id}, Distance: {dist}, <= Threshold: {dist <= threshold}");
                    }
                }
            }
        }
        
        // Direct in-memory filtering test
        _output.WriteLine("\n=== MANUAL FILTERING TEST ===");
        var allConstants = await _repository.GetAllAsync();
        var allList = allConstants.ToList();
        _output.WriteLine($"Total constants in DB: {allList.Count}");
        
        var activeConstants = allList.Where(c => c.Status == ConstantStatus.Active).ToList();
        _output.WriteLine($"Active constants: {activeConstants.Count}");
        
        var withCoordinates = activeConstants.Where(c => c.Coordinate != null).ToList();
        _output.WriteLine($"Active with coordinates: {withCoordinates.Count}");
        
        foreach (var c in withCoordinates)
        {
            var dist = c.Coordinate!.HilbertDistanceTo(center);
            var threshold = (ulong)(100000 * 1000);
            _output.WriteLine($"  Constant {c.Id}: Distance={dist}, <= {threshold}: {dist <= threshold}");
        }
    }

    [Fact]
    public async Task Diagnostic_CompareWorkingAndFailingTests()
    {
        // Test pattern from WORKING test (WithValidCenter)
        _output.WriteLine("=== WORKING TEST PATTERN ===");
        var center1 = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        var nearby1 = SpatialCoordinate.FromUniversalProperties(0, 510000, 760000, 150);

        var c1 = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        c1.SetCoordinateForTesting(center1);
        c1.ActivateForTesting();
        var c2 = Constant.Create(new byte[] { 2 }, ContentType.Binary);
        c2.SetCoordinateForTesting(nearby1);
        c2.ActivateForTesting();

        await _repository.AddAsync(c1);
        await _repository.AddAsync(c2);
        await _unitOfWork.SaveChangesAsync();

        var results1 = await _repository.GetNearbyConstantsAsync(center1, radius: 100000, maxResults: 10);
        var list1 = results1.ToList();
        
        _output.WriteLine($"Working test results: {list1.Count}");
        _output.WriteLine($"Contains c1: {list1.Any(c => c.Id == c1.Id)}");
        _output.WriteLine($"Contains c2: {list1.Any(c => c.Id == c2.Id)}");
        
        // Test pattern from FAILING test (OnlyReturnsActiveConstants)
        _output.WriteLine("\n=== FAILING TEST PATTERN ===");
        var center2 = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        var nearby2 = SpatialCoordinate.FromUniversalProperties(0, 510000, 760000, 150);

        var active = Constant.Create(new byte[] { 10 }, ContentType.Binary);
        active.SetCoordinateForTesting(nearby2);
        active.ActivateForTesting();

        await _repository.AddAsync(active);
        await _unitOfWork.SaveChangesAsync();

        var results2 = await _repository.GetNearbyConstantsAsync(center2, radius: 100000, maxResults: 10);
        var list2 = results2.ToList();
        
        _output.WriteLine($"Failing test results: {list2.Count}");
        _output.WriteLine($"Contains active: {list2.Any(c => c.Id == active.Id)}");
        
        // Compare coordinates
        _output.WriteLine("\n=== COORDINATE COMPARISON ===");
        _output.WriteLine($"center1 == center2: {center1.HilbertHigh == center2.HilbertHigh && center1.HilbertLow == center2.HilbertLow}");
        _output.WriteLine($"nearby1 == nearby2: {nearby1.HilbertHigh == nearby2.HilbertHigh && nearby1.HilbertLow == nearby2.HilbertLow}");
        _output.WriteLine($"c2 Hilbert: {c2.Coordinate?.HilbertHigh:X}/{c2.Coordinate?.HilbertLow:X}");
        _output.WriteLine($"active Hilbert: {active.Coordinate?.HilbertHigh:X}/{active.Coordinate?.HilbertLow:X}");
    }
}
