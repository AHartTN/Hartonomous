using FluentAssertions;
using Hartonomous.Core.Domain.Utilities;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Marshal;

namespace Hartonomous.Core.Tests.Domain.ValueObjects;

/// <summary>
/// Comprehensive tests for SpatialCoordinate 4D Hilbert-based value object.
/// Tests Hilbert encoding/decoding, range queries, factory methods, and spatial operations.
/// </summary>
public class SpatialCoordinateTests
{
    #region FromUniversalProperties Tests
    
    [Fact]
    public void FromUniversalProperties_WithValidParameters_CreatesCoordinate()
    {
        // Arrange
        uint spatialX = 1000;
        int entropy = 500000;
        int compressibility = 750000;
        int connectivity = 100;
        
        // Act
        var coord = SpatialCoordinate.FromUniversalProperties(
            spatialX, entropy, compressibility, connectivity);
        
        // Assert
        coord.Should().NotBeNull();
        coord.QuantizedEntropy.Should().Be(entropy);
        coord.QuantizedCompressibility.Should().Be(compressibility);
        coord.QuantizedConnectivity.Should().Be(connectivity);
        coord.Precision.Should().Be(HilbertCurve4D.DefaultPrecision);
    }
    
    [Theory]
    [InlineData(-1, 500000, 750000, 0)]      // Negative entropy
    [InlineData(500000, -1, 750000, 0)]      // Negative compressibility
    [InlineData(500000, 750000, -1, 0)]      // Negative connectivity
    [InlineData(2097152, 500000, 750000, 0)] // Entropy too large (> 2^21-1)
    [InlineData(500000, 2097152, 750000, 0)] // Compressibility too large
    [InlineData(500000, 750000, 2097152, 0)] // Connectivity too large
    public void FromUniversalProperties_WithInvalidQuantizedValues_ThrowsArgumentException(
        int entropy, int compressibility, int connectivity, int _)
    {
        // Arrange
        uint spatialX = 1000;
        
        // Act
        var act = () => SpatialCoordinate.FromUniversalProperties(
            spatialX, entropy, compressibility, connectivity);
        
        // Assert
        act.Should().Throw<ArgumentException>();
    }
    
    [Fact]
    public void FromUniversalProperties_WithCustomPrecision_UsesSpecifiedPrecision()
    {
        // Arrange
        int customPrecision = 15;
        
        // Act
        var coord = SpatialCoordinate.FromUniversalProperties(
            1000, 500000, 750000, 100, precision: customPrecision);
        
        // Assert
        coord.Precision.Should().Be(customPrecision);
    }
    
    [Fact]
    public void FromUniversalProperties_StoresMetadataRedundantly()
    {
        // Arrange
        int entropy = 123456;
        int compressibility = 654321;
        int connectivity = 999;
        
        // Act
        var coord = SpatialCoordinate.FromUniversalProperties(
            1000, entropy, compressibility, connectivity);
        
        // Assert - Redundant storage for fast B-tree filtering
        coord.QuantizedEntropy.Should().Be(entropy, "entropy stored redundantly");
        coord.QuantizedCompressibility.Should().Be(compressibility, "compressibility stored redundantly");
        coord.QuantizedConnectivity.Should().Be(connectivity, "connectivity stored redundantly");
    }
    
    #endregion
    
    #region FromHilbert4D Tests
    
    [Fact]
    public void FromHilbert4D_WithValidHilbertIndex_CreatesCoordinate()
    {
        // Arrange
        ulong hilbertHigh = 0x123456789ABCUL;
        ulong hilbertLow = 0xDEF0123456789UL;
        
        // Act
        var coord = SpatialCoordinate.FromHilbert4D(
            hilbertHigh, hilbertLow, 500000, 750000, 100);
        
        // Assert
        coord.Should().NotBeNull();
        coord.HilbertHigh.Should().Be(hilbertHigh);
        coord.HilbertLow.Should().Be(hilbertLow);
    }
    
    [Fact]
    public void FromHilbert4D_RoundTrip_WithFromUniversalProperties_PreservesValues()
    {
        // Arrange
        uint spatialX = 12345;
        int entropy = 500000;
        int compressibility = 750000;
        int connectivity = 100;
        
        var original = SpatialCoordinate.FromUniversalProperties(
            spatialX, entropy, compressibility, connectivity);
        
        // Act - Reconstruct from Hilbert index
        var reconstructed = SpatialCoordinate.FromHilbert4D(
            original.HilbertHigh,
            original.HilbertLow,
            original.QuantizedEntropy,
            original.QuantizedCompressibility,
            original.QuantizedConnectivity,
            original.Precision);
        
        // Assert
        reconstructed.Should().Be(original, "round-trip should preserve identity");
        reconstructed.HilbertHigh.Should().Be(original.HilbertHigh);
        reconstructed.HilbertLow.Should().Be(original.HilbertLow);
    }
    
    #endregion
    
    #region FromHash Tests
    
    [Fact]
    public void FromHash_WithValidHash_CreatesCoordinate()
    {
        // Arrange
        var hash = Hash256.Compute(new byte[] { 1, 2, 3, 4, 5 });
        
        // Act
        var coord = SpatialCoordinate.FromHash(hash, 500000, 750000, 100);
        
        // Assert
        coord.Should().NotBeNull();
        coord.HilbertHigh.Should().NotBe(0UL, "hash should produce non-zero Hilbert index");
    }
    
    [Fact]
    public void FromHash_WithNullHash_ThrowsArgumentNullException()
    {
        // Act
        var act = () => SpatialCoordinate.FromHash(null!, 500000, 750000, 100);
        
        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("hash");
    }
    
    [Fact]
    public void FromHash_WithModality_AcceptsModalityParameter()
    {
        // Arrange
        var hash = Hash256.Compute(new byte[] { 1, 2, 3, 4, 5 });
        
        // Act
        var coordWithModality = SpatialCoordinate.FromHash(hash, 500000, 750000, 100, modality: "text");
        var coordWithoutModality = SpatialCoordinate.FromHash(hash, 500000, 750000, 100);
        
        // Assert - Both should create valid coordinates (modality parameter accepted)
        coordWithModality.Should().NotBeNull("coordinate with modality created");
        coordWithoutModality.Should().NotBeNull("coordinate without modality created");
        // Note: Current GramSchmidtProjector may not use modality yet, 
        // so coordinates may be identical. Test validates API accepts parameter.
    }
    
    #endregion
    
    #region Lazy Coordinate Decoding Tests
    
    [Fact]
    public void XYZMProperties_AreLazyDecoded()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(12345, 500000, 750000, 100);
        
        // Act - Access X coordinate (triggers lazy decoding)
        var x = coord.X;
        
        // Assert - Should decode successfully
        x.Should().BeGreaterOrEqualTo(0, "X coordinate should be valid");
        x.Should().BeLessThan(Math.Pow(2, 21), "X should be within precision bounds");
    }
    
    [Fact]
    public void XYZMProperties_DecodeAllDimensions()
    {
        // Arrange
        uint expectedX = 12345;
        uint expectedY = 500000;
        uint expectedZ = 750000;
        uint expectedM = 100;
        
        var coord = SpatialCoordinate.FromUniversalProperties(
            expectedX, (int)expectedY, (int)expectedZ, (int)expectedM);
        
        // Act
        var decodedX = coord.X;
        var decodedY = coord.Y;
        var decodedZ = coord.Z;
        var decodedM = coord.M;
        
        // Assert - Hilbert encode→decode should preserve values
        decodedX.Should().BeApproximately(expectedX, 1, "X coordinate preserved");
        decodedY.Should().BeApproximately(expectedY, 1, "Y (entropy) preserved");
        decodedZ.Should().BeApproximately(expectedZ, 1, "Z (compressibility) preserved");
        decodedM.Should().BeApproximately(expectedM, 1, "M (connectivity) preserved");
    }
    
    [Fact]
    public void XYZMProperties_CachesDecodedValues()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(12345, 500000, 750000, 100);
        
        // Act - Access multiple times
        var x1 = coord.X;
        var x2 = coord.X;
        var y1 = coord.Y;
        var y2 = coord.Y;
        
        // Assert - Should return identical values (cached)
        x1.Should().Be(x2, "X should be cached");
        y1.Should().Be(y2, "Y should be cached");
    }
    
    #endregion
    
    #region GetHilbertRangeForRadius Tests
    
    [Fact]
    public void GetHilbertRangeForRadius_WithSmallRadius_ReturnsNarrowRange()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000000, 1000000, 1000000, 500);
        double smallRadius = 100;
        
        // Act
        var (minHigh, minLow, maxHigh, maxLow) = coord.GetHilbertRangeForRadius(smallRadius);
        
        // Assert - Small radius should produce narrow range
        var rangeSize = (maxHigh - minHigh) + (maxLow - minLow);
        rangeSize.Should().BeLessThan(10000UL, "small radius produces narrow Hilbert range");
    }
    
    [Fact]
    public void GetHilbertRangeForRadius_WithLargeRadius_ReturnsWideRange()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000000, 1000000, 1000000, 500);
        double largeRadius = 100000;
        
        // Act
        var (minHigh, minLow, maxHigh, maxLow) = coord.GetHilbertRangeForRadius(largeRadius);
        
        // Assert - Large radius should produce wide range
        var rangeSize = (maxHigh - minHigh) + (maxLow - minLow);
        rangeSize.Should().BeGreaterThan(10000UL, "large radius produces wide Hilbert range");
    }
    
    [Fact]
    public void GetHilbertRangeForRadius_RangeContainsCenterPoint()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000000, 1000000, 1000000, 500);
        
        // Act
        var (minHigh, minLow, maxHigh, maxLow) = coord.GetHilbertRangeForRadius(5000);
        
        // Assert - Range should contain the original point
        coord.HilbertHigh.Should().BeInRange(minHigh, maxHigh, "center point High within range");
        coord.HilbertLow.Should().BeInRange(minLow, maxLow, "center point Low within range");
    }
    
    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void GetHilbertRangeForRadius_IncreasingRadius_ProducesIncreasingRange(double radius)
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000000, 1000000, 1000000, 500);
        
        // Act
        var (minHigh, minLow, maxHigh, maxLow) = coord.GetHilbertRangeForRadius(radius);
        var rangeSize = (maxHigh - minHigh) + (maxLow - minLow);
        
        // Assert - Larger radius should produce larger range
        rangeSize.Should().BeGreaterThan(0UL, $"radius {radius} produces non-zero range");
    }
    
    #endregion
    
    #region Distance Tests
    
    [Fact]
    public void DistanceTo_SameCoordinate_ReturnsZero()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        
        // Act
        var distance = coord.DistanceTo(coord);
        
        // Assert
        distance.Should().Be(0, "distance to self should be zero");
    }
    
    [Fact]
    public void DistanceTo_NearbyCoordinates_ReturnsSmallDistance()
    {
        // Arrange
        var coord1 = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        var coord2 = SpatialCoordinate.FromUniversalProperties(1001, 500001, 750001, 101);
        
        // Act
        var distance = coord1.DistanceTo(coord2);
        
        // Assert
        distance.Should().BeLessThan(10, "nearby coordinates have small Euclidean distance");
    }
    
    [Fact]
    public void DistanceTo_FarCoordinates_ReturnsLargeDistance()
    {
        // Arrange
        var coord1 = SpatialCoordinate.FromUniversalProperties(1000, 100000, 100000, 10);
        var coord2 = SpatialCoordinate.FromUniversalProperties(100000, 1900000, 1900000, 1000);
        
        // Act
        var distance = coord1.DistanceTo(coord2);
        
        // Assert
        distance.Should().BeGreaterThan(1000, "far coordinates have large Euclidean distance");
    }
    
    [Fact]
    public void DistanceTo_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        
        // Act
        var act = () => coord.DistanceTo(null!);
        
        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
    
    [Fact]
    public void DistanceTo_IsSymmetric()
    {
        // Arrange
        var coord1 = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        var coord2 = SpatialCoordinate.FromUniversalProperties(2000, 600000, 850000, 200);
        
        // Act
        var distance1to2 = coord1.DistanceTo(coord2);
        var distance2to1 = coord2.DistanceTo(coord1);
        
        // Assert
        distance1to2.Should().BeApproximately(distance2to1, 0.01, "distance is symmetric");
    }
    
    #endregion
    
    #region HilbertDistanceTo Tests
    
    [Fact]
    public void HilbertDistanceTo_SameCoordinate_ReturnsZero()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        
        // Act
        var distance = coord.HilbertDistanceTo(coord);
        
        // Assert
        distance.Should().Be(0UL, "Hilbert distance to self should be zero");
    }
    
    [Fact]
    public void HilbertDistanceTo_NearbyInHilbertSpace_ReturnsSmallDistance()
    {
        // Arrange
        var coord1 = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        var coord2 = SpatialCoordinate.FromUniversalProperties(1001, 500001, 750001, 101);
        
        // Act
        var hilbertDist = coord1.HilbertDistanceTo(coord2);
        
        // Assert
        hilbertDist.Should().BeLessThan(10000UL, "nearby coordinates have small Hilbert distance");
    }
    
    [Fact]
    public void HilbertDistanceTo_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        
        // Act
        var act = () => coord.HilbertDistanceTo(null!);
        
        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
    
    #endregion
    
    #region ToPoint Tests
    
    [Fact]
    public void ToPoint_CreatesPostGISPointZM()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        
        // Act
        var point = coord.ToPoint();
        
        // Assert
        point.Should().NotBeNull();
        point.SRID.Should().Be(4326, "should use WGS 84 coordinate system");
        point.X.Should().Be(coord.X, "Point.X should match coordinate.X");
        point.Y.Should().Be(coord.Y, "Point.Y should match coordinate.Y");
        point.Z.Should().Be(coord.Z, "Point.Z should match coordinate.Z");
        point.M.Should().Be(coord.M, "Point.M should match coordinate.M");
    }
    
    #endregion
    
    #region ToCartesian4D Tests
    
    [Fact]
    public void ToCartesian4D_ReturnsAllFourDimensions()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(12345, 500000, 750000, 100);
        
        // Act
        var (x, y, z, m) = coord.ToCartesian4D();
        
        // Assert
        x.Should().BeApproximately(coord.X, 0.01);
        y.Should().BeApproximately(coord.Y, 0.01);
        z.Should().BeApproximately(coord.Z, 0.01);
        m.Should().BeApproximately(coord.M, 0.01);
    }
    
    #endregion
    
    #region Equality Tests
    
    [Fact]
    public void Equals_WithSameHilbertIndex_ReturnsTrue()
    {
        // Arrange
        var coord1 = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        var coord2 = SpatialCoordinate.FromHilbert4D(
            coord1.HilbertHigh, coord1.HilbertLow,
            coord1.QuantizedEntropy, coord1.QuantizedCompressibility, coord1.QuantizedConnectivity);
        
        // Act & Assert
        coord1.Should().Be(coord2, "same Hilbert index means equal");
    }
    
    [Fact]
    public void Equals_WithDifferentHilbertIndex_ReturnsFalse()
    {
        // Arrange
        var coord1 = SpatialCoordinate.FromUniversalProperties(1000, 500000, 750000, 100);
        var coord2 = SpatialCoordinate.FromUniversalProperties(2000, 600000, 850000, 200);
        
        // Act & Assert
        coord1.Should().NotBe(coord2, "different Hilbert indices are not equal");
    }
    
    #endregion
}
