using Hartonomous.Marshal;
using Xunit;

namespace Hartonomous.Marshal.Tests;

/// <summary>
/// Unit tests for HilbertCurve4D encoding/decoding.
/// Verifies correctness, locality preservation, and edge cases.
/// </summary>
public class HilbertCurve4DTests
{
    #region Encode/Decode Round-Trip Tests
    
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(100, 200, 300, 400)]
    [InlineData(1000, 2000, 3000, 4000)]
    [InlineData(2097151, 2097151, 2097151, 2097151)] // Max 21-bit values
    public void Encode_Decode_RoundTrip_PreservesCoordinates(
        uint x, uint y, uint z, uint m)
    {
        // Arrange & Act
        var (high, low) = HilbertCurve4D.Encode(x, y, z, m);
        var (decodedX, decodedY, decodedZ, decodedM) = HilbertCurve4D.Decode(high, low);
        
        // Assert
        Assert.Equal(x, decodedX);
        Assert.Equal(y, decodedY);
        Assert.Equal(z, decodedZ);
        Assert.Equal(m, decodedM);
    }
    
    [Fact]
    public void Encode_Decode_AllZeros_ReturnsZeros()
    {
        // Arrange
        uint x = 0, y = 0, z = 0, m = 0;
        
        // Act
        var (high, low) = HilbertCurve4D.Encode(x, y, z, m);
        var (dx, dy, dz, dm) = HilbertCurve4D.Decode(high, low);
        
        // Assert
        Assert.Equal(0u, dx);
        Assert.Equal(0u, dy);
        Assert.Equal(0u, dz);
        Assert.Equal(0u, dm);
    }
    
    [Fact]
    public void Encode_Decode_MaxValues_PreservesValues()
    {
        // Arrange
        uint max = (1u << 21) - 1; // 2^21 - 1 = 2,097,151
        
        // Act
        var (high, low) = HilbertCurve4D.Encode(max, max, max, max);
        var (dx, dy, dz, dm) = HilbertCurve4D.Decode(high, low);
        
        // Assert
        Assert.Equal(max, dx);
        Assert.Equal(max, dy);
        Assert.Equal(max, dz);
        Assert.Equal(max, dm);
    }
    
    #endregion
    
    #region Locality Preservation Tests
    
    [Fact]
    public void Encode_NearbyCoordinates_ProducesNearbyIndices()
    {
        // Arrange
        uint baseCoord = 1000;
        var (h1, l1) = HilbertCurve4D.Encode(baseCoord, baseCoord, baseCoord, baseCoord);
        var (h2, l2) = HilbertCurve4D.Encode(baseCoord + 1, baseCoord + 1, baseCoord + 1, baseCoord + 1);
        
        // Act
        var distance = HilbertCurve4D.Distance((h1, l1), (h2, l2));
        
        // Assert - nearby coordinates should have small Hilbert distance
        Assert.True(distance < 10000, $"Distance too large: {distance}");
    }
    
    [Fact]
    public void Encode_FarCoordinates_ProducesLargerDistance()
    {
        // Arrange
        var (h1, l1) = HilbertCurve4D.Encode(1000, 1000, 1000, 1000);
        var (h2, l2) = HilbertCurve4D.Encode(100000, 100000, 100000, 100000);
        
        // Act
        var distance = HilbertCurve4D.Distance((h1, l1), (h2, l2));
        
        // Assert - far coordinates should have larger distance
        Assert.True(distance > 10000, $"Distance too small: {distance}");
    }
    
    #endregion
    
    #region Precision Tests
    
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(21)]
    public void Encode_WithDifferentPrecisions_WorksCorrectly(int precision)
    {
        // Arrange
        uint max = (1u << precision) - 1;
        uint x = max / 2, y = max / 2, z = max / 2, m = max / 2;
        
        // Act
        var (high, low) = HilbertCurve4D.Encode(x, y, z, m, precision);
        var (dx, dy, dz, dm) = HilbertCurve4D.Decode(high, low, precision);
        
        // Assert
        Assert.Equal(x, dx);
        Assert.Equal(y, dy);
        Assert.Equal(z, dz);
        Assert.Equal(m, dm);
    }
    
    [Theory]
    [InlineData(0)]
    [InlineData(22)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Encode_WithInvalidPrecision_ThrowsArgumentException(int precision)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            HilbertCurve4D.Encode(100, 100, 100, 100, precision));
    }
    
    #endregion
    
    #region Edge Case Tests
    
    [Fact]
    public void Encode_CoordinatesExceedPrecision_ClampsToMax()
    {
        // Arrange
        uint exceeds = uint.MaxValue;
        int precision = 10; // Max value should be 1023
        uint expected = (1u << precision) - 1;
        
        // Act
        var (high, low) = HilbertCurve4D.Encode(exceeds, exceeds, exceeds, exceeds, precision);
        var (dx, dy, dz, dm) = HilbertCurve4D.Decode(high, low, precision);
        
        // Assert - should clamp to max value for precision
        Assert.Equal(expected, dx);
        Assert.Equal(expected, dy);
        Assert.Equal(expected, dz);
        Assert.Equal(expected, dm);
    }
    
    [Fact]
    public void Encode_SingleDimensionVaries_ProducesUniqueIndices()
    {
        // Arrange & Act
        var indices = new List<(ulong, ulong)>();
        for (uint x = 0; x < 10; x++)
        {
            indices.Add(HilbertCurve4D.Encode(x, 0, 0, 0));
        }
        
        // Assert - all indices should be unique
        Assert.Equal(10, indices.Distinct().Count());
    }
    
    #endregion
    
    #region Range Query Tests
    
    [Fact]
    public void GetRangeForRadius_WithZeroRadius_ReturnsNarrowRange()
    {
        // Arrange
        var (centerH, centerL) = HilbertCurve4D.Encode(1000, 1000, 1000, 1000);
        
        // Act
        var (min, max) = HilbertCurve4D.GetRangeForRadius((centerH, centerL), radius: 0);
        
        // Assert - should return same point (plus expansion factor)
        Assert.True(max.High >= min.High);
        Assert.True(max.High - min.High < 1000); // Should be relatively narrow
    }
    
    [Fact]
    public void GetRangeForRadius_WithLargeRadius_ReturnsWideRange()
    {
        // Arrange
        var (centerH, centerL) = HilbertCurve4D.Encode(100000, 100000, 100000, 100000);
        
        // Act
        var (min, max) = HilbertCurve4D.GetRangeForRadius((centerH, centerL), radius: 50000);
        
        // Assert - should return wide range (implementation modifies Low bits, High stays same)
        Assert.True(max.High >= min.High);
        Assert.True(max.Low > min.Low);
        Assert.True(max.Low - min.Low > 1000); // Radius creates wide span in Low bits
    }
    
    #endregion
    
    #region Performance Characteristic Tests
    
    [Fact]
    public void Encode_1000Iterations_CompletesQuickly()
    {
        // Arrange
        var random = new Random(42);
        var start = DateTime.UtcNow;
        
        // Act
        for (int i = 0; i < 1000; i++)
        {
            uint x = (uint)random.Next(0, 2097152);
            uint y = (uint)random.Next(0, 2097152);
            uint z = (uint)random.Next(0, 2097152);
            uint m = (uint)random.Next(0, 2097152);
            
            HilbertCurve4D.Encode(x, y, z, m);
        }
        
        var elapsed = DateTime.UtcNow - start;
        
        // Assert - should complete in reasonable time (<100ms for 1000 ops)
        Assert.True(elapsed.TotalMilliseconds < 100, 
            $"Encoding took too long: {elapsed.TotalMilliseconds}ms");
    }
    
    [Fact]
    public void Decode_1000Iterations_CompletesQuickly()
    {
        // Arrange
        var random = new Random(42);
        var indices = new List<(ulong, ulong)>();
        for (int i = 0; i < 1000; i++)
        {
            uint x = (uint)random.Next(0, 2097152);
            uint y = (uint)random.Next(0, 2097152);
            uint z = (uint)random.Next(0, 2097152);
            uint m = (uint)random.Next(0, 2097152);
            indices.Add(HilbertCurve4D.Encode(x, y, z, m));
        }
        
        var start = DateTime.UtcNow;
        
        // Act
        foreach (var (high, low) in indices)
        {
            HilbertCurve4D.Decode(high, low);
        }
        
        var elapsed = DateTime.UtcNow - start;
        
        // Assert - should complete in reasonable time (<100ms for 1000 ops)
        Assert.True(elapsed.TotalMilliseconds < 100, 
            $"Decoding took too long: {elapsed.TotalMilliseconds}ms");
    }
    
    #endregion
}
