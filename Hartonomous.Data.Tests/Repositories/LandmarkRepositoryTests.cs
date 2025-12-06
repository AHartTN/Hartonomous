using FluentAssertions;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;
using Hartonomous.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data.Tests.Repositories;

/// <summary>
/// Comprehensive tests for LandmarkRepository data access operations
/// Tests deterministic Hilbert tile-based landmarks, spatial containment, and proximity queries
/// </summary>
public class LandmarkRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly LandmarkRepository _repository;
    private readonly UnitOfWork _unitOfWork;

    public LandmarkRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new LandmarkRepository(_context);
        _unitOfWork = new UnitOfWork(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    #region GetByNameAsync Tests

    [Fact]
    public async Task GetByNameAsync_WithValidName_ReturnsLandmark()
    {
        // Arrange
        var landmark = Landmark.Create(
            hilbertPrefixHigh: 1000UL,
            hilbertPrefixLow: 2000UL,
            level: 15,
            description: "TestLandmark");

        await _repository.AddAsync(landmark);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var result = await _repository.GetByNameAsync(landmark.Name);

        // Assert
        result.Should().NotBeNull();
        result!.HilbertPrefixHigh.Should().Be(1000UL);
        result.Level.Should().Be(15);
    }

    [Fact]
    public async Task GetByNameAsync_WithNonExistentName_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByNameAsync("NonExistent");

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetByNameAsync_WithInvalidName_ThrowsArgumentException(string? invalidName)
    {
        // Act
        Func<Task> act = async () => await _repository.GetByNameAsync(invalidName!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public async Task GetByNameAsync_WithSoftDeletedLandmark_ReturnsNull()
    {
        // Arrange
        var landmark = Landmark.Create(1000UL, 2000UL, 15, "DeletedLandmark");
        landmark.Deactivate();
        // Note: Delete must be done through repository, not entity method

        await _repository.AddAsync(landmark);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var result = await _repository.GetByNameAsync("DeletedLandmark");

        // Assert
        result.Should().BeNull("soft-deleted landmarks filtered by query filter");
    }

    #endregion

    #region GetContainingLandmarksAsync Tests

    [Fact]
    public async Task GetContainingLandmarksAsync_ReturnsLandmarkContainingCoordinate()
    {
        // Arrange
        var coordinate = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100, precision: 15);
        var landmark = Landmark.Create(
            hilbertPrefixHigh: coordinate.HilbertHigh,
            hilbertPrefixLow: coordinate.HilbertLow,
            level: 15);

        await _repository.AddAsync(landmark);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetContainingLandmarksAsync(coordinate);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(l => l.Id == landmark.Id);
    }

    [Fact]
    public async Task GetContainingLandmarksAsync_WithNullCoordinate_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _repository.GetContainingLandmarksAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("coordinate");
    }

    [Fact]
    public async Task GetContainingLandmarksAsync_OnlyReturnsActiveLandmarks()
    {
        // Arrange
        var coordinate = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100, precision: 15);
        var activeLandmark = Landmark.Create(coordinate.HilbertHigh, coordinate.HilbertLow, 15, "Active");
        var inactiveLandmark = Landmark.Create(coordinate.HilbertHigh, coordinate.HilbertLow, 15, "Inactive");
        inactiveLandmark.Deactivate();

        await _repository.AddAsync(activeLandmark);
        await _repository.AddAsync(inactiveLandmark);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetContainingLandmarksAsync(coordinate);

        // Assert
        results.Should().Contain(l => l.Id == activeLandmark.Id);
        results.Should().NotContain(l => l.Id == inactiveLandmark.Id);
    }

    #endregion

    #region GetNearbyLandmarksAsync Tests

    [Fact]
    public async Task GetNearbyLandmarksAsync_ReturnsLandmarksWithinDistance()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 1000000, 1000000, 1000, precision: 15);
        var nearby = SpatialCoordinate.FromUniversalProperties(0, 1100000, 1100000, 1100, precision: 15);
        var far = SpatialCoordinate.FromUniversalProperties(100, 1500000, 1800000, 1000, precision: 15);

        var l1 = Landmark.Create(center.HilbertHigh, center.HilbertLow, 15, "Center");
        var l2 = Landmark.Create(nearby.HilbertHigh, nearby.HilbertLow, 15, "Nearby");
        var l3 = Landmark.Create(far.HilbertHigh, far.HilbertLow, 15, "Far");

        await _repository.AddAsync(l1);
        await _repository.AddAsync(l2);
        await _repository.AddAsync(l3);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetNearbyLandmarksAsync(center, maxDistance: 10000000);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(l => l.Id == l1.Id);
        results.Should().Contain(l => l.Id == l2.Id);
    }

    [Fact]
    public async Task GetNearbyLandmarksAsync_WithNullCenter_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _repository.GetNearbyLandmarksAsync(null!, maxDistance: 10);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("center");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetNearbyLandmarksAsync_WithInvalidDistance_ThrowsArgumentException(double invalidDistance)
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100, precision: 15);

        // Act
        Func<Task> act = async () => await _repository.GetNearbyLandmarksAsync(center, maxDistance: invalidDistance);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("maxDistance");
    }

    [Fact]
    public async Task GetNearbyLandmarksAsync_OnlyReturnsActiveLandmarks()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 1000000, 1000000, 1000, precision: 15);
        var nearby = SpatialCoordinate.FromUniversalProperties(0, 1100000, 1100000, 1100, precision: 15);

        var active = Landmark.Create(nearby.HilbertHigh, nearby.HilbertLow, 15, "Active");
        var inactive = Landmark.Create(nearby.HilbertHigh, nearby.HilbertLow, 15, "Inactive");
        inactive.Deactivate();

        await _repository.AddAsync(active);
        await _repository.AddAsync(inactive);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetNearbyLandmarksAsync(center, maxDistance: 10000000);

        // Assert
        results.Should().Contain(l => l.Id == active.Id);
        results.Should().NotContain(l => l.Id == inactive.Id);
    }

    #endregion
}
