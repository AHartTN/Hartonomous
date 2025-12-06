using FluentAssertions;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.IntegrationTests.Builders;
using Hartonomous.Data.Repositories;
using Xunit;

namespace Hartonomous.Data.IntegrationTests;

/// <summary>
/// Integration tests for ConstantRepository spatial queries against real PostgreSQL + PostGIS.
/// These tests verify database-side spatial functions work correctly with indexes.
/// Uses real Docker container with 125-constant spatial grid (seeded automatically).
/// </summary>
[Collection("PostgreSQL")]
public class ConstantRepositorySpatialTests : IClassFixture<PostgreSqlFixture>
{
    private readonly IConstantRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PostgreSqlFixture _fixture;

    public ConstantRepositorySpatialTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _repository = new ConstantRepository(fixture.DbContext!);
        _unitOfWork = fixture.UnitOfWork!;
    }

    #region GetNearbyConstantsAsync Tests

    /// <summary>
    /// Test GetNearbyConstantsAsync from grid center.
    /// Grid center (Y=1.25M, Z=1.25M, M=1000) should have 26 neighbors in 3×3×3 cube.
    /// Seeded grid: 5×5×5 = 125 constants with 500k spacing.
    /// </summary>
    [Fact]
    public async Task GetNearbyConstantsAsync_FromGridCenter_ReturnsNeighbors()
    {
        // Arrange - Grid center in 5×5×5 grid
        var center = SpatialCoordinate.FromUniversalProperties(0, 1_250_000, 1_250_000, 1000);

        // Act - Query with radius that captures 3×3×3 cube (spacing is 500k)
        // Euclidean distance to corner of cube: sqrt(3 * 500k²) ≈ 866k
        var results = await _repository.GetNearbyConstantsAsync(center, radius: 900_000, maxResults: 50);

        // Assert - Should return center + 26 neighbors = 27 total
        results.Should().HaveCount(27);
        results.Should().OnlyContain(c => c.Coordinate != null);
    }

    [Fact]
    public async Task GetNearbyConstantsAsync_WithNullCenter_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _repository.GetNearbyConstantsAsync(null!, radius: 10);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("center");
    }

    [Fact]
    public async Task GetNearbyConstantsAsync_WithNegativeRadius_ThrowsArgumentException()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);

        // Act
        Func<Task> act = async () => await _repository.GetNearbyConstantsAsync(center, radius: -5);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("radius");
    }

    /// <summary>
    /// Test GetNearbyConstantsAsync from grid corner.
    /// Corner (Y=250k, Z=250k, M=0) should have only 7 neighbors (one octant of cube).
    /// </summary>
    [Fact]
    public async Task GetNearbyConstantsAsync_FromGridCorner_ReturnsOctant()
    {
        // Arrange - Grid corner (minimum coordinates)
        var corner = SpatialCoordinate.FromUniversalProperties(0, 250_000, 250_000, 0);

        // Act - Same radius as center test
        var results = await _repository.GetNearbyConstantsAsync(corner, radius: 900_000, maxResults: 50);

        // Assert - Corner has only 1 octant (2×2×2 cube) = 8 constants (corner + 7 neighbors)
        results.Should().HaveCount(8);
    }

    /// <summary>
    /// Test GetNearbyConstantsAsync respects maxResults parameter.
    /// </summary>
    [Fact]
    public async Task GetNearbyConstantsAsync_RespectsMaxResults()
    {
        // Arrange - Grid center with many neighbors
        var center = SpatialCoordinate.FromUniversalProperties(0, 1_250_000, 1_250_000, 1000);

        // Act - Request only 10 results despite 27 being within radius
        var results = await _repository.GetNearbyConstantsAsync(center, radius: 900_000, maxResults: 10);

        // Assert - Should return exactly 10
        results.Should().HaveCount(10);
    }

    /// <summary>
    /// Test spatial query with custom cluster using TestDataFactory.
    /// Verifies that custom test data works alongside seeded grid.
    /// </summary>
    [Fact]
    public async Task GetNearbyConstantsAsync_WithCustomCluster_ReturnsAll()
    {
        // Arrange - Create tight cluster using TestDataFactory (separate from seeded grid)
        var clusterCenter = SpatialCoordinate.FromUniversalProperties(0, 500_000, 500_000, 500);
        var cluster = TestDataFactory.CreateSpatialCluster(clusterCenter, count: 20, radius: 10_000);

        foreach (var constant in cluster)
        {
            await _repository.AddAsync(constant);
        }
        await _unitOfWork.SaveChangesAsync();

        // Act - Query with slightly larger radius
        var results = await _repository.GetNearbyConstantsAsync(clusterCenter, radius: 15_000, maxResults: 50);

        // Assert - Should return all 20 cluster constants (tight cluster within radius)
        // Note: Seeded grid has constant at (250k, 250k, 0) which is ~354k away, outside radius
        results.Should().HaveCountGreaterThanOrEqualTo(20);
    }

    #endregion

    #region GetKNearestConstantsAsync Tests

    /// <summary>
    /// Test GetKNearestConstantsAsync returns exactly K results.
    /// </summary>
    [Fact]
    public async Task GetKNearestConstantsAsync_ReturnsExactlyK()
    {
        // Arrange - Query from arbitrary point (125 constants in seeded grid)
        var queryPoint = SpatialCoordinate.FromUniversalProperties(0, 500_000, 750_000, 100);

        // Act - Request 15 nearest
        var results = await _repository.GetKNearestConstantsAsync(queryPoint, k: 15);

        // Assert - Should return exactly 15
        results.Should().HaveCount(15);
    }

    [Fact]
    public async Task GetKNearestConstantsAsync_WithNullCenter_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _repository.GetKNearestConstantsAsync(null!, k: 10);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("center");
    }

    /// <summary>
    /// Test GetKNearestConstantsAsync with K=0 throws ArgumentException.
    /// </summary>
    [Fact]
    public async Task GetKNearestConstantsAsync_WithZeroK_ThrowsArgumentException()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500_000, 500_000, 100);

        // Act
        Func<Task> act = async () => await _repository.GetKNearestConstantsAsync(center, k: 0);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("k");
    }

    /// <summary>
    /// Test GetKNearestConstantsAsync with negative K throws ArgumentException.
    /// </summary>
    [Fact]
    public async Task GetKNearestConstantsAsync_WithNegativeK_ThrowsArgumentException()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500_000, 500_000, 100);

        // Act
        Func<Task> act = async () => await _repository.GetKNearestConstantsAsync(center, k: -5);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("k");
    }

    /// <summary>
    /// Test GetKNearestConstantsAsync orders by distance correctly.
    /// </summary>
    [Fact]
    public async Task GetKNearestConstantsAsync_OrdersByDistance()
    {
        // Arrange - Query from known grid point
        var gridPoint = SpatialCoordinate.FromUniversalProperties(0, 750_000, 750_000, 500);

        // Act - Get 20 nearest
        var results = await _repository.GetKNearestConstantsAsync(gridPoint, k: 20);

        // Assert - Results should be ordered by increasing Euclidean distance in YZM space
        results.Should().HaveCount(20);
        var distances = results.Select(c => c.Coordinate!.DistanceTo(gridPoint)).ToList();
        distances.Should().BeInAscendingOrder();
    }

    /// <summary>
    /// Test k-NN with custom test data using ConstantBuilder.
    /// Verifies that builder-created constants integrate correctly with spatial queries.
    /// </summary>
    [Fact]
    public async Task GetKNearestConstantsAsync_WithBuilderData_FindsCustomConstants()
    {
        // Arrange - Create test data at known distances using ConstantBuilder
        var center = SpatialCoordinate.FromUniversalProperties(0, 500_000, 500_000, 100);
        
        // Create 3 custom constants at exact distances
        var close = new ConstantBuilder()
            .WithData(new byte[] { 0xFF, 0x01 })
            .WithContentType(ContentType.Text)
            .WithCoordinate(505_000, 505_000, 110)  // ~7071 distance
            .Active()
            .Build();
        
        var medium = new ConstantBuilder()
            .WithData(new byte[] { 0xFF, 0x02 })
            .WithContentType(ContentType.Text)
            .WithCoordinate(550_000, 550_000, 150)  // ~70711 distance
            .Active()
            .Build();
        
        var far = new ConstantBuilder()
            .WithData(new byte[] { 0xFF, 0x03 })
            .WithContentType(ContentType.Text)
            .WithCoordinate(700_000, 700_000, 500)  // ~282843 distance
            .Active()
            .Build();
        
        await _repository.AddAsync(close);
        await _repository.AddAsync(medium);
        await _repository.AddAsync(far);
        await _unitOfWork.SaveChangesAsync();

        // Act - Get 3 nearest (should prefer our custom constants if they're closest)
        var results = await _repository.GetKNearestConstantsAsync(center, k: 3);

        // Assert - Results should be ordered by distance
        results.Should().HaveCount(3);
        var distances = results.Select(c => c.Coordinate!.DistanceTo(center)).ToList();
        distances.Should().BeInAscendingOrder();
        
        // Our close constant should be in top 3 (very close to query point)
        results.Should().Contain(c => c.Id == close.Id, "custom close constant should be in k-NN result");
    }

    #endregion
}
