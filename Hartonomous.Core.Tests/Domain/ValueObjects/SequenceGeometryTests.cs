using Hartonomous.Core.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Hartonomous.Core.Tests.Domain.ValueObjects;

public class SequenceGeometryTests
{
    [Fact]
    public void FromCoordinates_WithValidInput_CreatesSequenceGeometry()
    {
        // Arrange
        var constantIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var coordinates = new List<SpatialCoordinate>
        {
            SpatialCoordinate.FromUniversalProperties(100, 50, 50, 10),
            SpatialCoordinate.FromUniversalProperties(200, 60, 55, 15),
            SpatialCoordinate.FromUniversalProperties(300, 70, 60, 20)
        };

        // Act
        var sequence = SequenceGeometry.FromCoordinates(constantIds, coordinates);

        // Assert
        sequence.Should().NotBeNull();
        sequence.Length.Should().Be(3);
        sequence.ConstantIds.Should().BeEquivalentTo(constantIds);
        sequence.Geometry.Should().NotBeNull();
        sequence.Geometry.Coordinates.Should().HaveCount(3);
    }

    [Fact]
    public void FromCoordinates_WithEmptyIds_ThrowsArgumentException()
    {
        // Arrange
        var constantIds = new List<Guid>();
        var coordinates = new List<SpatialCoordinate>
        {
            SpatialCoordinate.FromUniversalProperties(100, 50, 50, 10)
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            SequenceGeometry.FromCoordinates(constantIds, coordinates));
    }

    [Fact]
    public void FromCoordinates_WithMismatchedCounts_ThrowsArgumentException()
    {
        // Arrange
        var constantIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var coordinates = new List<SpatialCoordinate>
        {
            SpatialCoordinate.FromUniversalProperties(100, 50, 50, 10)
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            SequenceGeometry.FromCoordinates(constantIds, coordinates));
    }

    [Fact]
    public void DetectGaps_WithLargeGaps_ReturnsGaps()
    {
        // Arrange - Create sequence with gaps
        var constantIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var coordinates = new List<SpatialCoordinate>
        {
            SpatialCoordinate.FromUniversalProperties(100, 50, 50, 10),
            SpatialCoordinate.FromUniversalProperties(5000, 60, 55, 15), // Large gap
            SpatialCoordinate.FromUniversalProperties(5100, 70, 60, 20)
        };
        var sequence = SequenceGeometry.FromCoordinates(constantIds, coordinates);

        // Act
        var gaps = sequence.DetectGaps(gapThreshold: 1000).ToList();

        // Assert
        gaps.Should().NotBeEmpty();
        gaps[0].GapSize.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void DetectGaps_WithNoGaps_ReturnsEmpty()
    {
        // Arrange - Create sequence with small distances
        var constantIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var coordinates = new List<SpatialCoordinate>
        {
            SpatialCoordinate.FromUniversalProperties(100, 50, 50, 10),
            SpatialCoordinate.FromUniversalProperties(150, 60, 55, 15),
            SpatialCoordinate.FromUniversalProperties(200, 70, 60, 20)
        };
        var sequence = SequenceGeometry.FromCoordinates(constantIds, coordinates);

        // Act
        var gaps = sequence.DetectGaps(gapThreshold: 1000).ToList();

        // Assert
        gaps.Should().BeEmpty();
    }

    [Fact]
    public void GetCentroid_ReturnsAverageCoordinate()
    {
        // Arrange
        var constantIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var coordinates = new List<SpatialCoordinate>
        {
            SpatialCoordinate.FromUniversalProperties(100, 50, 50, 10),
            SpatialCoordinate.FromUniversalProperties(200, 60, 60, 20)
        };
        var sequence = SequenceGeometry.FromCoordinates(constantIds, coordinates);

        // Act
        var centroid = sequence.GetCentroid();

        // Assert
        centroid.Should().NotBeNull();
        // Centroid should be roughly in the middle
        centroid.X.Should().BeGreaterThan(coordinates[0].X);
        centroid.X.Should().BeLessThan(coordinates[1].X);
    }

    [Fact]
    public void HilbertLength_CalculatesCorrectly()
    {
        // Arrange
        var constantIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var coordinates = new List<SpatialCoordinate>
        {
            SpatialCoordinate.FromUniversalProperties(100, 50, 50, 10),
            SpatialCoordinate.FromUniversalProperties(200, 60, 55, 15),
            SpatialCoordinate.FromUniversalProperties(350, 70, 60, 20)
        };

        // Act
        var sequence = SequenceGeometry.FromCoordinates(constantIds, coordinates);

        // Assert
        sequence.HilbertLength.Should().BeGreaterThan(0, "Hilbert length should be non-zero for different coordinates");
        // The actual length depends on Hilbert encoding, just verify it's calculated
    }
}
