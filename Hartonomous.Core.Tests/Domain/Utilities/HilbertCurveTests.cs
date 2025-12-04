using Hartonomous.Core.Domain.Utilities;

namespace Hartonomous.Core.Tests.Domain.Utilities;

/// <summary>
/// Unit tests for Hilbert curve 3D encoding/decoding
/// </summary>
public class HilbertCurveTests
{
    [Fact]
    public void Encode_WithValidCoordinates_ReturnsNonZeroIndex()
    {
        // Arrange
        double x = 100.0;
        double y = 200.0;
        double z = 300.0;

        // Act
        var index = HilbertCurve.Encode(x, y, z);

        // Assert
        index.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Encode_WithOrigin_ReturnsZero()
    {
        // Arrange
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;

        // Act
        var index = HilbertCurve.Encode(x, y, z);

        // Assert
        index.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(10, 20, 30)]
    [InlineData(100, 200, 300)]
    [InlineData(1000, 2000, 3000)]
    [InlineData(10000, 20000, 30000)]
    public void EncodeAndDecode_RoundTrip_PreservesCoordinates(double x, double y, double z)
    {
        // Arrange & Act
        var encoded = HilbertCurve.Encode(x, y, z);
        var (decodedX, decodedY, decodedZ) = HilbertCurve.Decode(encoded);

        // Assert - Exact match expected for bit-interleaving implementation
        decodedX.Should().Be(Math.Floor(x));
        decodedY.Should().Be(Math.Floor(y));
        decodedZ.Should().Be(Math.Floor(z));
    }

    [Fact]
    public void Encode_WithSameCoordinates_ReturnsSameIndex()
    {
        // Arrange
        double x = 123.456;
        double y = 789.012;
        double z = 345.678;

        // Act
        var index1 = HilbertCurve.Encode(x, y, z);
        var index2 = HilbertCurve.Encode(x, y, z);

        // Assert
        index1.Should().Be(index2);
    }

    [Fact]
    public void Encode_WithDifferentCoordinates_ReturnsDifferentIndices()
    {
        // Arrange
        double x1 = 100.0, y1 = 200.0, z1 = 300.0;
        double x2 = 101.0, y2 = 201.0, z2 = 301.0;

        // Act
        var index1 = HilbertCurve.Encode(x1, y1, z1);
        var index2 = HilbertCurve.Encode(x2, y2, z2);

        // Assert
        index1.Should().NotBe(index2);
    }

    [Fact]
    public void Encode_WithNearbyPoints_ProducesSimilarIndices()
    {
        // Arrange - Two points close in 3D space
        double x1 = 1000.0, y1 = 2000.0, z1 = 3000.0;
        double x2 = 1001.0, y2 = 2001.0, z2 = 3001.0;

        // Act
        var index1 = HilbertCurve.Encode(x1, y1, z1);
        var index2 = HilbertCurve.Encode(x2, y2, z2);

        // Assert - Nearby points should have similar Hilbert indices (locality preservation)
        var difference = Math.Abs((long)index1 - (long)index2);
        difference.Should().BeLessThan(1000000); // Reasonable locality threshold
    }

    [Fact]
    public void Encode_WithMaxCoordinates_ReturnsMaxIndex()
    {
        // Arrange
        ulong maxValue = (1UL << HilbertCurve.DefaultPrecision) - 1;
        double max = maxValue;

        // Act
        var index = HilbertCurve.Encode(max, max, max);

        // Assert
        index.Should().BeGreaterThan(0);
        index.Should().BeLessThanOrEqualTo((1UL << (3 * HilbertCurve.DefaultPrecision)) - 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(22)]
    [InlineData(100)]
    public void Encode_WithInvalidPrecision_ThrowsArgumentException(int precision)
    {
        // Arrange
        double x = 100.0, y = 200.0, z = 300.0;

        // Act
        Action act = () => HilbertCurve.Encode(x, y, z, precision);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Precision must be between 1 and*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(21)]
    public void Encode_WithVariousPrecisions_SuccessfullyEncodes(int precision)
    {
        // Arrange
        double x = 100.0, y = 200.0, z = 300.0;

        // Act
        var index = HilbertCurve.Encode(x, y, z, precision);

        // Assert
        index.Should().BeGreaterThanOrEqualTo(0);
        index.Should().BeLessThanOrEqualTo((1UL << (3 * precision)) - 1);
    }

    [Fact]
    public void GetRangeForRadius_WithValidInputs_ReturnsValidRange()
    {
        // Arrange
        var centerIndex = HilbertCurve.Encode(1000, 2000, 3000);
        double radius = 100.0;

        // Act
        var (minIndex, maxIndex) = HilbertCurve.GetRangeForRadius(centerIndex, radius);

        // Assert
        minIndex.Should().BeLessThan(maxIndex);
        minIndex.Should().BeLessThanOrEqualTo(centerIndex);
        maxIndex.Should().BeGreaterThanOrEqualTo(centerIndex);
    }

    [Fact]
    public void GetRangeForRadius_WithZeroRadius_ReturnsValidRange()
    {
        // Arrange
        var centerIndex = HilbertCurve.Encode(1000, 2000, 3000);
        double radius = 0.0;

        // Act
        var (minIndex, maxIndex) = HilbertCurve.GetRangeForRadius(centerIndex, radius);

        // Assert - Range should be valid (min <= center <= max) with possible expansion
        minIndex.Should().BeLessThanOrEqualTo(centerIndex);
        maxIndex.Should().BeGreaterThanOrEqualTo(minIndex);
    }

    [Fact]
    public void GetRangeForRadius_WithNegativeRadius_ThrowsArgumentException()
    {
        // Arrange
        var centerIndex = HilbertCurve.Encode(1000, 2000, 3000);
        double radius = -10.0;

        // Act
        Action act = () => HilbertCurve.GetRangeForRadius(centerIndex, radius);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Radius must be non-negative*");
    }

    [Fact]
    public void GetRangeForRadius_WithLargerRadius_ReturnsLargerRange()
    {
        // Arrange
        var centerIndex = HilbertCurve.Encode(1000, 2000, 3000);
        double smallRadius = 10.0;
        double largeRadius = 100.0;

        // Act
        var (minSmall, maxSmall) = HilbertCurve.GetRangeForRadius(centerIndex, smallRadius);
        var (minLarge, maxLarge) = HilbertCurve.GetRangeForRadius(centerIndex, largeRadius);

        // Assert
        var smallRange = maxSmall - minSmall;
        var largeRange = maxLarge - minLarge;
        largeRange.Should().BeGreaterThan(smallRange);
    }

    [Fact]
    public void Decode_WithValidIndex_ReturnsValidCoordinates()
    {
        // Arrange
        ulong index = 1000000;

        // Act
        var (x, y, z) = HilbertCurve.Decode(index);

        // Assert
        x.Should().BeGreaterThanOrEqualTo(0);
        y.Should().BeGreaterThanOrEqualTo(0);
        z.Should().BeGreaterThanOrEqualTo(0);
        
        ulong maxValue = (1UL << HilbertCurve.DefaultPrecision) - 1;
        x.Should().BeLessThanOrEqualTo(maxValue);
        y.Should().BeLessThanOrEqualTo(maxValue);
        z.Should().BeLessThanOrEqualTo(maxValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(22)]
    public void Decode_WithInvalidPrecision_ThrowsArgumentException(int precision)
    {
        // Arrange
        ulong index = 1000000;

        // Act
        Action act = () => HilbertCurve.Decode(index, precision);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Precision must be between 1 and*");
    }

    [Fact]
    public void Encode_LocalityPreservation_NearbyPointsHaveCloseIndices()
    {
        // Arrange - Generate a grid of nearby points
        var points = new List<(double x, double y, double z)>();
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 3; k++)
                    points.Add((1000 + i * 10, 2000 + j * 10, 3000 + k * 10));

        // Act - Encode all points
        var indices = points.Select(p => HilbertCurve.Encode(p.x, p.y, p.z)).ToList();

        // Assert - Calculate average distance between consecutive indices
        var distances = new List<long>();
        for (int i = 0; i < indices.Count - 1; i++)
        {
            distances.Add(Math.Abs((long)indices[i] - (long)indices[i + 1]));
        }

        var avgDistance = distances.Average();
        
        // Nearby points in 3D should have relatively small Hilbert index differences
        avgDistance.Should().BeLessThan(10000000); // Reasonable threshold for locality
    }

    [Fact]
    public void Encode_Deterministic_SameInputProducesSameOutput()
    {
        // Arrange
        var testCases = new[]
        {
            (x: 0.0, y: 0.0, z: 0.0),
            (x: 1.0, y: 1.0, z: 1.0),
            (x: 12345.6789, y: 98765.4321, z: 55555.5555),
            (x: 1000000.0, y: 2000000.0, z: 1500000.0)
        };

        foreach (var (x, y, z) in testCases)
        {
            // Act
            var index1 = HilbertCurve.Encode(x, y, z);
            var index2 = HilbertCurve.Encode(x, y, z);

            // Assert
            index1.Should().Be(index2, $"encoding ({x}, {y}, {z}) should be deterministic");
        }
    }

    [Fact]
    public void Encode_WithNegativeCoordinates_ClampsToZero()
    {
        // Arrange
        double x = -100.0;
        double y = -200.0;
        double z = -300.0;

        // Act
        var index = HilbertCurve.Encode(x, y, z);

        // Assert - Should treat negatives as zero
        var expectedIndex = HilbertCurve.Encode(0, 0, 0);
        index.Should().Be(expectedIndex);
    }

    [Fact]
    public void Encode_WithExcessiveCoordinates_ClampsToMax()
    {
        // Arrange
        ulong maxValue = (1UL << HilbertCurve.DefaultPrecision) - 1;
        double excessive = maxValue * 10.0;

        // Act
        var index1 = HilbertCurve.Encode(excessive, excessive, excessive);
        var index2 = HilbertCurve.Encode(maxValue, maxValue, maxValue);

        // Assert - Should clamp to max
        index1.Should().Be(index2);
    }
}
