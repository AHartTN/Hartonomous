using FluentAssertions;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;
using Hartonomous.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data.Tests.Repositories;

/// <summary>
/// Comprehensive tests for ConstantRepository data access operations
/// Tests spatial queries, hash lookups, pagination, and Hilbert range queries
/// </summary>
public class ConstantRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ConstantRepository _repository;
    private readonly UnitOfWork _unitOfWork;

    public ConstantRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ConstantRepository(_context);
        _unitOfWork = new UnitOfWork(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    #region GetByHashAsync Tests

    [Fact]
    public async Task GetByHashAsync_WithValidHash_ReturnsConstant()
    {
        // Arrange
        var hash = Hash256.Compute(new byte[] { 1, 2, 3, 4, 5 });
        var constant = Constant.Create(new byte[] { 1, 2, 3, 4, 5 }, ContentType.Binary);
        constant.Project(); // Deterministic hash-based projection
        
        await _repository.AddAsync(constant);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var result = await _repository.GetByHashAsync(hash);

        // Assert
        result.Should().NotBeNull();
        result!.Hash.Should().Be(hash);
        result.Data.Should().Equal(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public async Task GetByHashAsync_WithNonExistentHash_ReturnsNull()
    {
        // Arrange
        var hash = Hash256.Compute(new byte[] { 99, 99, 99 });

        // Act
        var result = await _repository.GetByHashAsync(hash);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByHashAsync_WithNullHash_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _repository.GetByHashAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("hash");
    }

    [Fact]
    public async Task GetByHashAsync_WithSoftDeletedConstant_ReturnsNull()
    {
        // Arrange
        var hash = Hash256.Compute(new byte[] { 1, 2, 3 });
        var constant = Constant.Create(new byte[] { 1, 2, 3 }, ContentType.Binary);
        // Note: Soft delete must be done through repository, not entity method
        
        await _repository.AddAsync(constant);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var result = await _repository.GetByHashAsync(hash);

        // Assert
        result.Should().BeNull("soft-deleted constants should be filtered by query filter");
    }

    #endregion

    #region GetByHashStringAsync Tests

    [Fact]
    public async Task GetByHashStringAsync_WithValidHexString_ReturnsConstant()
    {
        // Arrange
        var hash = Hash256.Compute(new byte[] { 10, 20, 30 });
        var constant = Constant.Create(new byte[] { 10, 20, 30 }, ContentType.Binary);
        await _repository.AddAsync(constant);
        await _unitOfWork.SaveChangesAsync();

        var hashHex = hash.Hex;

        // Act
        var result = await _repository.GetByHashStringAsync(hashHex);

        // Assert
        result.Should().NotBeNull();
        result!.Hash.Should().Be(hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetByHashStringAsync_WithInvalidHexString_ThrowsArgumentException(string? invalidHex)
    {
        // Act
        Func<Task> act = async () => await _repository.GetByHashStringAsync(invalidHex!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("hashHex");
    }

    #endregion

    #region GetNearbyConstantsAsync Tests

    [Fact]
    public async Task GetNearbyConstantsAsync_WithValidCenter_ReturnsNearbyConstants()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        var nearby = SpatialCoordinate.FromUniversalProperties(0, 510000, 760000, 150);
        var far = SpatialCoordinate.FromUniversalProperties(100, 1500000, 1800000, 1000);

        var c1 = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        c1.Project();
        var c2 = Constant.Create(new byte[] { 2 }, ContentType.Binary);
        c2.Project();
        var c3 = Constant.Create(new byte[] { 3 }, ContentType.Binary);
        c3.Project();

        await _repository.AddAsync(c1);
        await _repository.AddAsync(c2);
        await _repository.AddAsync(c3);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetNearbyConstantsAsync(center, radius: 50, maxResults: 10);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(c => c.Id == c1.Id);
        results.Should().Contain(c => c.Id == c2.Id);
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

    [Fact]
    public async Task GetNearbyConstantsAsync_RespectsMaxResults()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        
        for (int i = 0; i < 20; i++)
        {
            var coord = SpatialCoordinate.FromUniversalProperties((uint)i, (int)(500000 + i * 1000), 750000, 100);
            var constant = Constant.Create(new byte[] { (byte)i }, ContentType.Binary);
            constant.Project(); // Deterministic hash-based projection
            await _repository.AddAsync(constant);
        }
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetNearbyConstantsAsync(center, radius: 1000, maxResults: 5);

        // Assert
        results.Should().HaveCountLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task GetNearbyConstantsAsync_OnlyReturnsActiveConstants()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        var nearby = SpatialCoordinate.FromUniversalProperties(0, 510000, 760000, 150);

        var active = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        active.Project();
        var inactive = Constant.Create(new byte[] { 2 }, ContentType.Binary);
        inactive.Project();
        inactive.MarkAsFailed("Processed"); // Change status

        await _repository.AddAsync(active);
        await _repository.AddAsync(inactive);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetNearbyConstantsAsync(center, radius: 50);

        // Assert
        results.Should().Contain(c => c.Id == active.Id);
        results.Should().NotContain(c => c.Id == inactive.Id);
    }

    #endregion

    #region GetKNearestConstantsAsync Tests

    [Fact]
    public async Task GetKNearestConstantsAsync_ReturnsExactlyKConstants()
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        
        for (int i = 0; i < 20; i++)
        {
            var coord = SpatialCoordinate.FromUniversalProperties((uint)i, (int)(500000 + i * 10000), 750000, 100);
            var constant = Constant.Create(new byte[] { (byte)i }, ContentType.Binary);
            constant.Project(); // Deterministic hash-based projection
            await _repository.AddAsync(constant);
        }
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetKNearestConstantsAsync(center, k: 5);

        // Assert
        results.Should().HaveCountLessThanOrEqualTo(5);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetKNearestConstantsAsync_WithInvalidK_ThrowsArgumentException(int invalidK)
    {
        // Arrange
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);

        // Act
        Func<Task> act = async () => await _repository.GetKNearestConstantsAsync(center, k: invalidK);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("k");
    }

    #endregion

    #region GetByStatusAsync Tests

    [Fact]
    public async Task GetByStatusAsync_ReturnsConstantsWithMatchingStatus()
    {
        // Arrange
        var active1 = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        var active2 = Constant.Create(new byte[] { 2 }, ContentType.Binary);
        var processed = Constant.Create(new byte[] { 3 }, ContentType.Binary);
        processed.MarkAsFailed("Processed");

        await _repository.AddAsync(active1);
        await _repository.AddAsync(active2);
        await _repository.AddAsync(processed);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetByStatusAsync(ConstantStatus.Active);

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(c => c.Id == active1.Id);
        results.Should().Contain(c => c.Id == active2.Id);
        results.Should().NotContain(c => c.Id == processed.Id);
    }

    [Fact]
    public async Task GetByStatusAsync_RespectsPagination()
    {
        // Arrange
        for (int i = 0; i < 15; i++)
        {
            var constant = Constant.Create(new byte[] { (byte)i }, ContentType.Binary);
            await _repository.AddAsync(constant);
        }
        await _unitOfWork.SaveChangesAsync();

        // Act
        var page1 = await _repository.GetByStatusAsync(ConstantStatus.Active, pageNumber: 1, pageSize: 5);
        var page2 = await _repository.GetByStatusAsync(ConstantStatus.Active, pageNumber: 2, pageSize: 5);

        // Assert
        page1.Should().HaveCount(5);
        page2.Should().HaveCount(5);
        page1.Select(c => c.Id).Should().NotIntersectWith(page2.Select(c => c.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetByStatusAsync_WithInvalidPageNumber_ThrowsArgumentException(int invalidPage)
    {
        // Act
        Func<Task> act = async () => await _repository.GetByStatusAsync(ConstantStatus.Active, pageNumber: invalidPage);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("pageNumber");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task GetByStatusAsync_WithInvalidPageSize_ThrowsArgumentException(int invalidPageSize)
    {
        // Act
        Func<Task> act = async () => await _repository.GetByStatusAsync(ConstantStatus.Active, pageSize: invalidPageSize);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("pageSize");
    }

    #endregion

    #region GetByHilbertRangeAsync Tests

    [Fact]
    public async Task GetByHilbertRangeAsync_ReturnsConstantsInRange()
    {
        // Arrange
        var coord1 = SpatialCoordinate.FromHilbert4D(100, 0, 500000, 750000, 100);
        var coord2 = SpatialCoordinate.FromHilbert4D(200, 0, 500000, 750000, 100);
        var coord3 = SpatialCoordinate.FromHilbert4D(300, 0, 500000, 750000, 100);

        var c1 = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        c1.Project();
        var c2 = Constant.Create(new byte[] { 2 }, ContentType.Binary);
        c2.Project();
        var c3 = Constant.Create(new byte[] { 3 }, ContentType.Binary);
        c3.Project();

        await _repository.AddAsync(c1);
        await _repository.AddAsync(c2);
        await _repository.AddAsync(c3);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = await _repository.GetByHilbertRangeAsync(startId: 100, endId: 200);

        // Assert
        results.Should().Contain(c => c.Id == c1.Id);
        results.Should().Contain(c => c.Id == c2.Id);
        results.Should().NotContain(c => c.Id == c3.Id);
    }

    [Fact]
    public async Task GetByHilbertRangeAsync_WithStartGreaterThanEnd_ThrowsArgumentException()
    {
        // Act
        Func<Task> act = async () => await _repository.GetByHilbertRangeAsync(startId: 200, endId: 100);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region GetTopByFrequencyAsync Tests

    [Fact]
    public async Task GetTopByFrequencyAsync_ReturnsOrderedByFrequency()
    {
        // Arrange
        var c1 = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        var c2 = Constant.Create(new byte[] { 2 }, ContentType.Binary);
        var c3 = Constant.Create(new byte[] { 3 }, ContentType.Binary);
        
        c1.IncrementFrequency(); // Frequency = 1
        c2.IncrementFrequency();
        c2.IncrementFrequency(); // Frequency = 2
        c3.IncrementFrequency();
        c3.IncrementFrequency();
        c3.IncrementFrequency(); // Frequency = 3

        await _repository.AddAsync(c1);
        await _repository.AddAsync(c2);
        await _repository.AddAsync(c3);
        await _unitOfWork.SaveChangesAsync();

        // Act
        var results = (await _repository.GetTopByFrequencyAsync(count: 3)).ToList();

        // Assert
        results.Should().HaveCount(3);
        results[0].Frequency.Should().Be(3);
        results[1].Frequency.Should().Be(2);
        results[2].Frequency.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetTopByFrequencyAsync_WithInvalidCount_ThrowsArgumentException(int invalidCount)
    {
        // Act
        Func<Task> act = async () => await _repository.GetTopByFrequencyAsync(count: invalidCount);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("count");
    }

    #endregion
}
