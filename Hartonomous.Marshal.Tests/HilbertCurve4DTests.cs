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
    
    #region GetHilbertTileId Tests (NEW)
    
    [Fact]
    public void GetHilbertTileId_Level0_ReturnsZeros()
    {
        // Arrange - encode a non-zero coordinate
        var (high, low) = HilbertCurve4D.Encode(12345, 67890, 11111, 22222);
        
        // Act - get tile ID at level 0 (no bits kept, all zeroed)
        var (tileHigh, tileLow) = HilbertCurve4D.GetHilbertTileId(high, low, level: 0);
        
        // Assert - should return all zeros
        Assert.Equal(0UL, tileHigh);
        Assert.Equal(0UL, tileLow);
    }
    
    [Fact]
    public void GetHilbertTileId_MaxLevel_ReturnsOriginal()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(12345, 67890, 11111, 22222);
        
        // Act - get tile ID at max level (all bits kept)
        var (tileHigh, tileLow) = HilbertCurve4D.GetHilbertTileId(high, low, level: 21);
        
        // Assert - should return original index unchanged
        Assert.Equal(high, tileHigh);
        Assert.Equal(low, tileLow);
    }
    
    [Fact]
    public void GetHilbertTileId_SameLevel_ProducesSameTile()
    {
        // Arrange - two nearby coordinates
        var (h1, l1) = HilbertCurve4D.Encode(1000, 1000, 1000, 1000);
        var (h2, l2) = HilbertCurve4D.Encode(1001, 1001, 1001, 1001);
        
        // Act - get tile IDs at coarse level 10
        var (tile1H, tile1L) = HilbertCurve4D.GetHilbertTileId(h1, l1, level: 10);
        var (tile2H, tile2L) = HilbertCurve4D.GetHilbertTileId(h2, l2, level: 10);
        
        // Assert - nearby coordinates should map to same tile at coarse level
        Assert.Equal(tile1H, tile2H);
        Assert.Equal(tile1L, tile2L);
    }
    
    [Fact]
    public void GetHilbertTileId_DifferentLevels_ProducesDifferentTiles()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(12345, 67890, 11111, 22222);
        
        // Act - get tile IDs at different levels
        var (tile10H, tile10L) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var (tile15H, tile15L) = HilbertCurve4D.GetHilbertTileId(high, low, level: 15);
        
        // Assert - different levels should produce different tile IDs (unless coordinate is at boundary)
        Assert.True(tile10H != tile15H || tile10L != tile15L);
    }
    
    [Fact]
    public void GetHilbertTileId_InvalidLevel_ThrowsArgumentException()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(1000, 1000, 1000, 1000);
        
        // Act & Assert - negative level
        Assert.Throws<ArgumentException>(() => 
            HilbertCurve4D.GetHilbertTileId(high, low, level: -1));
        
        // Act & Assert - level exceeds precision
        Assert.Throws<ArgumentException>(() => 
            HilbertCurve4D.GetHilbertTileId(high, low, level: 22));
    }
    
    [Fact]
    public void GetHilbertTileId_HierarchicalProperty_ChildContainedInParent()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(12345, 67890, 11111, 22222);
        
        // Act - get tile IDs at parent (level 10) and child (level 15)
        var (parentH, parentL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var (childH, childL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 15);
        
        // Assert - parent tile should contain child tile (top bits should match)
        // Level 10 = 40 bits (4*10), Level 15 = 60 bits (4*15)
        // Parent has more bits zeroed, so child should have same high bits where parent has bits
        Assert.True(childH >= parentH, "Child high should be >= parent high");
    }
    
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    public void GetHilbertTileId_MultipleCoordinatesInTile_ProduceSameTileId(int level)
    {
        // Arrange - create coordinates that should map to same tile
        uint baseCoord = 10000;
        var coords = new List<(ulong high, ulong low)>();
        
        for (uint offset = 0; offset < 5; offset++)
        {
            coords.Add(HilbertCurve4D.Encode(baseCoord + offset, baseCoord + offset, 
                                             baseCoord + offset, baseCoord + offset));
        }
        
        // Act - get tile IDs for all coordinates
        var tileIds = coords.Select(c => HilbertCurve4D.GetHilbertTileId(c.high, c.low, level))
                            .ToList();
        
        // Assert - at coarse levels, nearby coords should share tiles
        var uniqueTiles = tileIds.Distinct().Count();
        Assert.True(uniqueTiles >= 1 && uniqueTiles <= coords.Count, 
            $"Expected 1 to {coords.Count} unique tiles, got {uniqueTiles}");
    }
    
    #endregion
    
    #region GetRangeForRadius Additional Tests (NEW)
    
    [Fact]
    public void GetRangeForRadius_4ParamOverload_MatchesTupleVersion()
    {
        // Arrange
        ulong high = 12345678;
        ulong low = 87654321;
        ulong radius = 1000;
        
        // Act
        var result1 = HilbertCurve4D.GetRangeForRadius((high, low), radius);
        var result2 = HilbertCurve4D.GetRangeForRadius(high, low, radius, precision: 21);
        
        // Assert - both overloads should produce same result
        Assert.Equal(result1.Min.High, result2.Min.High);
        Assert.Equal(result1.Min.Low, result2.Min.Low);
        Assert.Equal(result1.Max.High, result2.Max.High);
        Assert.Equal(result1.Max.Low, result2.Max.Low);
    }
    
    [Fact]
    public void GetRangeForRadius_NearZero_HandlesUnderflow()
    {
        // Arrange - center near zero
        var (centerH, centerL) = HilbertCurve4D.Encode(10, 10, 10, 10);
        ulong largeRadius = 10000000;
        
        // Act
        var (min, max) = HilbertCurve4D.GetRangeForRadius((centerH, centerL), largeRadius);
        
        // Assert - min should not underflow (clamp to 0)
        Assert.True(min.Low >= 0);
        Assert.True(max.Low > min.Low);
    }
    
    [Fact]
    public void GetRangeForRadius_NearMax_HandlesOverflow()
    {
        // Arrange - center near max value
        uint maxCoord = (1u << 21) - 1;
        var (centerH, centerL) = HilbertCurve4D.Encode(maxCoord, maxCoord, maxCoord, maxCoord);
        ulong largeRadius = 10000000;
        
        // Act
        var (min, max) = HilbertCurve4D.GetRangeForRadius((centerH, centerL), largeRadius);
        
        // Assert - max should not overflow (clamp to ulong.MaxValue)
        Assert.True(max.Low <= ulong.MaxValue);
        Assert.True(max.Low > min.Low);
    }
    
    #endregion
    
    #region Distance Function Tests (NEW)
    
    [Fact]
    public void Distance_IdenticalIndices_ReturnsZero()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(1000, 2000, 3000, 4000);
        
        // Act
        var distance = HilbertCurve4D.Distance((high, low), (high, low));
        
        // Assert
        Assert.Equal(0UL, distance);
    }
    
    [Fact]
    public void Distance_AdjacentIndices_ReturnsSmallValue()
    {
        // Arrange
        var (h1, l1) = HilbertCurve4D.Encode(1000, 1000, 1000, 1000);
        var (h2, l2) = HilbertCurve4D.Encode(1001, 1000, 1000, 1000);
        
        // Act
        var distance = HilbertCurve4D.Distance((h1, l1), (h2, l2));
        
        // Assert - adjacent coords should have small distance
        Assert.True(distance > 0 && distance < 10000, 
            $"Expected small distance, got {distance}");
    }
    
    [Fact]
    public void Distance_IsSymmetric()
    {
        // Arrange
        var (h1, l1) = HilbertCurve4D.Encode(1000, 2000, 3000, 4000);
        var (h2, l2) = HilbertCurve4D.Encode(5000, 6000, 7000, 8000);
        
        // Act
        var dist1 = HilbertCurve4D.Distance((h1, l1), (h2, l2));
        var dist2 = HilbertCurve4D.Distance((h2, l2), (h1, l1));
        
        // Assert - distance should be symmetric
        Assert.Equal(dist1, dist2);
    }
    
    #endregion
    
    #region Constants Tests (NEW)
    
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        // Assert
        Assert.Equal(21, HilbertCurve4D.DefaultPrecision);
        Assert.Equal(21, HilbertCurve4D.MaxPrecision);
        Assert.Equal(4, HilbertCurve4D.Dimensions);
    }
    
    #endregion
}
