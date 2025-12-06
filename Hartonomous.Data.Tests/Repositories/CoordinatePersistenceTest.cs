using Xunit;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;
using Hartonomous.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data.Tests.Repositories;

public class CoordinatePersistenceTest : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ConstantRepository _repository;
    private readonly UnitOfWork _unitOfWork;

    public CoordinatePersistenceTest()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ConstantRepository(_context);
        _unitOfWork = new UnitOfWork(_context);
    }

    [Fact]
    public async Task Coordinate_Is_Persisted_And_Reloaded()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100);
        var constant = Constant.Create(new byte[] { 1 }, ContentType.Binary);
        constant.SetCoordinateForTesting(coord);
        constant.ActivateForTesting();

        await _repository.AddAsync(constant);
        await _unitOfWork.SaveChangesAsync();

        // Clear context to force reload from database
        _context.ChangeTracker.Clear();

        // Act - reload from database
        var reloaded = await _context.Constants
            .FirstOrDefaultAsync(c => c.Id == constant.Id);

        // Assert
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded!.Coordinate);
        Assert.Equal(ConstantStatus.Active, reloaded.Status);
        Assert.Equal(500000, reloaded.Coordinate!.Y);
        Assert.Equal(750000, reloaded.Coordinate.Z);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
