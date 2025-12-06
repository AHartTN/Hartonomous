using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;
using Hartonomous.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data.Tests.Repositories;

public class NearbyQueryDebugTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ApplicationDbContext _context;
    private readonly ConstantRepository _repository;
    private readonly UnitOfWork _unitOfWork;

    public NearbyQueryDebugTest(ITestOutputHelper output)
    {
        _output = output;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ConstantRepository(_context);
        _unitOfWork = new UnitOfWork(_context);
    }

    [Fact]
    public async Task Debug_GetNearbyConstantsAsync()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        var nearby = SpatialCoordinate.FromUniversalProperties(0, 510000, 760000, 150);

        _output.WriteLine($"Center: X={center.X}, Y={center.Y}, Z={center.Z}, M={center.M}");
        _output.WriteLine($"Nearby: X={nearby.X}, Y={nearby.Y}, Z={nearby.Z}, M={nearby.M}");
        _output.WriteLine($"Euclidean distance: {center.DistanceTo(nearby):F2}");
        _output.WriteLine("");

        var active = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        active.SetCoordinateForTesting(nearby);
        active.ActivateForTesting();

        _output.WriteLine($"Before save - Active status: {active.Status}, Coordinate: {active.Coordinate != null}");

        await _repository.AddAsync(active);
        await _unitOfWork.SaveChangesAsync();

        _output.WriteLine($"After save - Active ID: {active.Id}");

        // Clear and reload to simulate real query
        _context.ChangeTracker.Clear();

        // Check what's in the database
        var all = await _context.Constants.ToListAsync();
        _output.WriteLine($"Total constants in DB: {all.Count}");
        foreach (var c in all)
        {
            _output.WriteLine($"  - ID: {c.Id}, Status: {c.Status}, Coordinate: {c.Coordinate != null}");
            if (c.Coordinate != null)
            {
                _output.WriteLine($"    Coord: X={c.Coordinate.X}, Y={c.Coordinate.Y}, Z={c.Coordinate.Z}, M={c.Coordinate.M}");
                _output.WriteLine($"    Distance from center: {c.Coordinate.DistanceTo(center):F2}");
            }
        }
        _output.WriteLine("");

        // Now test the actual query
        var results = await _repository.GetNearbyConstantsAsync(center, radius: 10000000000, maxResults: 10);

        _output.WriteLine($"Query results: {results.Count()}");
        foreach (var r in results)
        {
            _output.WriteLine($"  - ID: {r.Id}, Status: {r.Status}");
        }

        // Assert
        results.Should().Contain(c => c.Id == active.Id, "active constant should be found");
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
