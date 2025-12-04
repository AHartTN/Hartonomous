using FluentAssertions;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Tests.Domain.ValueObjects;

public class SpatialCoordinateInterpolateTests
{
    [Fact]
    public void Interpolate_EmptyList_ThrowsArgumentException()
    {
        var coordinates = new List<SpatialCoordinate>();

        var act = () => SpatialCoordinate.Interpolate(coordinates);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Cannot interpolate empty coordinate list*");
    }

    [Fact]
    public void Interpolate_SingleCoordinate_ReturnsSameCoordinate()
    {
        var coord = SpatialCoordinate.Create(100, 200, 300);
        var coordinates = new List<SpatialCoordinate> { coord };

        var result = SpatialCoordinate.Interpolate(coordinates);

        result.Should().Be(coord, "single coordinate should return itself");
    }

    [Fact]
    public void Interpolate_TwoCoordinates_ReturnsCartesianMidpoint()
    {
        var coord1 = SpatialCoordinate.Create(0, 0, 0);
        var coord2 = SpatialCoordinate.Create(100, 200, 300);
        var coordinates = new List<SpatialCoordinate> { coord1, coord2 };

        var result = SpatialCoordinate.Interpolate(coordinates);

        // Should average: (0+100)/2 = 50, (0+200)/2 = 100, (0+300)/2 = 150
        result.X.Should().BeApproximately(50, 1, "X should be averaged");
        result.Y.Should().BeApproximately(100, 1, "Y should be averaged");
        result.Z.Should().BeApproximately(150, 1, "Z should be averaged");
    }

    [Fact]
    public void Interpolate_ThreeCoordinates_ReturnsCartesianCentroid()
    {
        var coord1 = SpatialCoordinate.Create(0, 0, 0);
        var coord2 = SpatialCoordinate.Create(300, 0, 0);
        var coord3 = SpatialCoordinate.Create(0, 300, 0);
        var coordinates = new List<SpatialCoordinate> { coord1, coord2, coord3 };

        var result = SpatialCoordinate.Interpolate(coordinates);

        // Centroid: (0+300+0)/3 = 100, (0+0+300)/3 = 100, (0+0+0)/3 = 0
        result.X.Should().BeApproximately(100, 1);
        result.Y.Should().BeApproximately(100, 1);
        result.Z.Should().BeApproximately(0, 1);
    }

    [Fact]
    public void Interpolate_FourCoordinates_ReturnsAverage()
    {
        var coord1 = SpatialCoordinate.Create(10, 20, 30);
        var coord2 = SpatialCoordinate.Create(20, 40, 60);
        var coord3 = SpatialCoordinate.Create(30, 60, 90);
        var coord4 = SpatialCoordinate.Create(40, 80, 120);
        var coordinates = new List<SpatialCoordinate> { coord1, coord2, coord3, coord4 };

        var result = SpatialCoordinate.Interpolate(coordinates);

        // Average: (10+20+30+40)/4 = 25, (20+40+60+80)/4 = 50, (30+60+90+120)/4 = 75
        result.X.Should().BeApproximately(25, 1);
        result.Y.Should().BeApproximately(50, 1);
        result.Z.Should().BeApproximately(75, 1);
    }

    [Fact]
    public void Interpolate_WithoutExplicitPrecision_UsesFirstCoordinatePrecision()
    {
        var coord1 = SpatialCoordinate.Create(100, 200, 300, precision: 15);
        var coord2 = SpatialCoordinate.Create(200, 400, 600, precision: 21);
        var coordinates = new List<SpatialCoordinate> { coord1, coord2 };

        var result = SpatialCoordinate.Interpolate(coordinates);

        result.Precision.Should().Be(15, "should use first coordinate's precision when not specified");
    }

    [Fact]
    public void Interpolate_WithExplicitPrecision_UsesProvidedPrecision()
    {
        var coord1 = SpatialCoordinate.Create(100, 200, 300, precision: 15);
        var coord2 = SpatialCoordinate.Create(200, 400, 600, precision: 21);
        var coordinates = new List<SpatialCoordinate> { coord1, coord2 };

        var result = SpatialCoordinate.Interpolate(coordinates, precision: 10);

        result.Precision.Should().Be(10, "should use explicitly provided precision");
    }

    [Fact]
    public void Interpolate_IdenticalCoordinates_ReturnsSameLocation()
    {
        var coord = SpatialCoordinate.Create(500, 600, 700);
        var coordinates = new List<SpatialCoordinate> 
        { 
            coord, 
            SpatialCoordinate.Create(500, 600, 700),
            SpatialCoordinate.Create(500, 600, 700)
        };

        var result = SpatialCoordinate.Interpolate(coordinates);

        result.X.Should().BeApproximately(500, 1);
        result.Y.Should().BeApproximately(600, 1);
        result.Z.Should().BeApproximately(700, 1);
    }

    [Fact]
    public void Interpolate_LargeNumberOfCoordinates_ComputesCorrectly()
    {
        var coordinates = new List<SpatialCoordinate>();
        for (int i = 0; i < 1000; i++)
        {
            coordinates.Add(SpatialCoordinate.Create(i, i * 2, i * 3));
        }

        var result = SpatialCoordinate.Interpolate(coordinates);

        // Average of 0..999: sum = n*(n-1)/2 = 1000*999/2 = 499500, avg = 499.5
        result.X.Should().BeApproximately(499.5, 5);
        result.Y.Should().BeApproximately(999, 10);   // 499.5 * 2
        result.Z.Should().BeApproximately(1498.5, 15); // 499.5 * 3
    }

    [Fact]
    public void Interpolate_BoundaryCoordinates_HandlesEdgeCases()
    {
        var coord1 = SpatialCoordinate.Create(0, 0, 0);
        var coord2 = SpatialCoordinate.Create(2097151, 2097151, 2097151); // Max for 21-bit precision
        var coordinates = new List<SpatialCoordinate> { coord1, coord2 };

        var result = SpatialCoordinate.Interpolate(coordinates);

        // Should be roughly in middle of range
        result.X.Should().BeApproximately(1048575.5, 1000);
        result.Y.Should().BeApproximately(1048575.5, 1000);
        result.Z.Should().BeApproximately(1048575.5, 1000);
    }

    [Fact]
    public void Interpolate_PreservesHilbertEncoding()
    {
        var coord1 = SpatialCoordinate.Create(100, 200, 300);
        var coord2 = SpatialCoordinate.Create(200, 400, 600);
        var coordinates = new List<SpatialCoordinate> { coord1, coord2 };

        var result = SpatialCoordinate.Interpolate(coordinates);

        // Result should have valid Hilbert index (non-zero for non-origin points)
        result.HilbertIndex.Should().BeGreaterThan(0ul, "interpolated coordinate should have valid Hilbert encoding");
    }

    [Fact]
    public void Interpolate_MixedPrecisionCoordinates_AveragesCartesianCorrectly()
    {
        var coord1 = SpatialCoordinate.Create(100, 100, 100, precision: 10);
        var coord2 = SpatialCoordinate.Create(200, 200, 200, precision: 21);
        var coordinates = new List<SpatialCoordinate> { coord1, coord2 };

        var result = SpatialCoordinate.Interpolate(coordinates);

        // Even with mixed precision, Cartesian averaging should work
        // (coordinates decode to their stored X,Y,Z values)
        result.X.Should().BeApproximately(150, 50); // Wider tolerance for mixed precision
        result.Y.Should().BeApproximately(150, 50);
        result.Z.Should().BeApproximately(150, 50);
    }
}
