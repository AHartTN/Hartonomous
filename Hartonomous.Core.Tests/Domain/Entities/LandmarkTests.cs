using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Marshal;
using Xunit;

namespace Hartonomous.Core.Tests.Domain.Entities;

public sealed class LandmarkTests
{
    #region Factory Method Tests
    
    [Fact]
    public void Create_WithValidParameters_CreatesLandmark()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(1000, 1000, 1000, 1000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        int level = 10;
        string description = "Test Landmark";
        
        // Act
        var landmark = Landmark.Create(prefixH, prefixL, level, description);
        
        // Assert
        Assert.Equal(prefixH, landmark.HilbertPrefixHigh);
        Assert.Equal(prefixL, landmark.HilbertPrefixLow);
        Assert.Equal(level, landmark.Level);
        Assert.Equal(description, landmark.Description);
        Assert.True(landmark.IsActive);
        Assert.Equal(0, landmark.ConstantCount);
        Assert.NotNull(landmark.Name);
        Assert.Contains($"L{level}", landmark.Name);
    }
    
    [Fact]
    public void Create_WithoutDescription_CreatesLandmarkWithNullDescription()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(1000, 1000, 1000, 1000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        
        // Act
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Assert
        Assert.Null(landmark.Description);
        Assert.True(landmark.IsActive);
    }
    
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(22)]
    [InlineData(100)]
    public void Create_WithInvalidLevel_ThrowsArgumentException(int invalidLevel)
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(1000, 1000, 1000, 1000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            Landmark.Create(prefixH, prefixL, invalidLevel));
    }
    
    #endregion
    
    #region Center Property Tests
    
    [Fact]
    public void Center_LazilyCalculated_ReturnsValidCoordinate()
    {
        // Arrange
        uint x = 10000, y = 20000, z = 30000, m = 40000;
        var (high, low) = HilbertCurve4D.Encode(x, y, z, m);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act
        var center = landmark.Center;
        
        // Assert - center should be a valid coordinate
        Assert.NotNull(center);
        Assert.True(center.X >= 0 && center.X <= (1u << 21) - 1);
        Assert.True(center.Y >= 0 && center.Y <= (1 << 21) - 1);
    }
    
    [Fact]
    public void Radius_CalculatesBasedOnLevel()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act
        var radius = landmark.Radius;
        
        // Assert - radius should be 2^(21-10) = 2^11 = 2048
        Assert.Equal(2048, radius);
    }
    
    #endregion
    
    #region ContainsHilbertIndex Tests
    
    [Fact]
    public void ContainsHilbertIndex_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act
        bool contains = landmark.ContainsHilbertIndex((high, low));
        
        // Assert
        Assert.True(contains);
    }
    
    [Fact]
    public void ContainsHilbertIndex_WithinTileBoundary_ReturnsTrue()
    {
        // Arrange - create landmark at level 10
        var (tileHigh, tileLow) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(tileHigh, tileLow, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act - check if nearby coordinate is contained
        var (nearbyH, nearbyL) = HilbertCurve4D.Encode(10100, 10100, 10100, 10100);
        bool contains = landmark.ContainsHilbertIndex((nearbyH, nearbyL));
        
        // Assert - should be true if nearby coords map to same tile
        Assert.True(contains);
    }
    
    [Fact]
    public void ContainsHilbertIndex_OutsideTileBoundary_ReturnsFalse()
    {
        // Arrange
        var (tileHigh, tileLow) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(tileHigh, tileLow, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act - check far away coordinate
        var (farH, farL) = HilbertCurve4D.Encode(1000000, 1000000, 1000000, 1000000);
        bool contains = landmark.ContainsHilbertIndex((farH, farL));
        
        // Assert
        Assert.False(contains);
    }
    
    #endregion
    
    #region ContainsPoint Tests
    
    [Fact]
    public void ContainsPoint_WithPointInTile_ReturnsTrue()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(coord.HilbertHigh, coord.HilbertLow, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act
        bool contains = landmark.ContainsPoint(coord);
        
        // Assert
        Assert.True(contains);
    }
    
    [Fact]
    public void ContainsPoint_WithPointOutsideTile_ReturnsFalse()
    {
        // Arrange
        var tileCoord = SpatialCoordinate.FromUniversalProperties(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(tileCoord.HilbertHigh, tileCoord.HilbertLow, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act
        var farCoord = SpatialCoordinate.FromUniversalProperties(1000000, 1000000, 1000000, 1000000);
        bool contains = landmark.ContainsPoint(farCoord);
        
        // Assert
        Assert.False(contains);
    }
    
    [Fact]
    public void ContainsPoint_WithNullPoint_ThrowsArgumentNullException()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => landmark.ContainsPoint(null!));
    }
    
    #endregion
    
    #region Hierarchical Relationship Tests
    
    [Fact]
    public void Landmarks_AtDifferentLevels_ShowHierarchy()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(10000, 10000, 10000, 10000);
        
        // Create parent at level 10 and child at level 15
        var (prefixParentH, prefixParentL) = HilbertCurve4D.GetHilbertTileId(
            coord.HilbertHigh, coord.HilbertLow, level: 10);
        var (prefixChildH, prefixChildL) = HilbertCurve4D.GetHilbertTileId(
            coord.HilbertHigh, coord.HilbertLow, level: 15);
        
        var parent = Landmark.Create(prefixParentH, prefixParentL, 10, "Parent");
        var child = Landmark.Create(prefixChildH, prefixChildL, 15, "Child");
        
        // Act & Assert - both should contain the original coordinate
        Assert.True(parent.ContainsPoint(coord));
        Assert.True(child.ContainsPoint(coord));
        
        // Parent should have lower level (coarser granularity)
        Assert.True(parent.Level < child.Level);
        
        // Parent should have larger radius
        Assert.True(parent.Radius > child.Radius);
    }
    
    [Fact]
    public void Landmarks_SameLevel_DifferentRegions_DontOverlap()
    {
        // Arrange
        var coord1 = SpatialCoordinate.FromUniversalProperties(10000, 10000, 10000, 10000);
        var coord2 = SpatialCoordinate.FromUniversalProperties(1000000, 1000000, 1000000, 1000000);
        
        var (prefix1H, prefix1L) = HilbertCurve4D.GetHilbertTileId(
            coord1.HilbertHigh, coord1.HilbertLow, level: 10);
        var (prefix2H, prefix2L) = HilbertCurve4D.GetHilbertTileId(
            coord2.HilbertHigh, coord2.HilbertLow, level: 10);
        
        var landmark1 = Landmark.Create(prefix1H, prefix1L, 10, "Region1");
        var landmark2 = Landmark.Create(prefix2H, prefix2L, 10, "Region2");
        
        // Act & Assert
        Assert.True(landmark1.ContainsPoint(coord1));
        Assert.False(landmark1.ContainsPoint(coord2));
        Assert.False(landmark2.ContainsPoint(coord1));
        Assert.True(landmark2.ContainsPoint(coord2));
    }
    
    #endregion
    
    #region Activation/Deactivation Tests
    
    [Fact]
    public void Reactivate_SetsIsActiveTrue()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        landmark.Deactivate(); // First deactivate
        
        // Act
        landmark.Reactivate();
        
        // Assert
        Assert.True(landmark.IsActive);
    }
    
    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act
        landmark.Deactivate();
        
        // Assert
        Assert.False(landmark.IsActive);
    }
    
    [Fact]
    public void Create_NewLandmark_IsActiveByDefault()
    {
        // Arrange & Act
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Assert
        Assert.True(landmark.IsActive);
    }
    
    #endregion
    
    #region Statistics Tests
    
    [Fact]
    public void UpdateStatistics_UpdatesConstantCountAndDensity()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        var initialCount = landmark.ConstantCount;
        
        // Act
        landmark.UpdateStatistics(150);
        
        // Assert
        Assert.Equal(150, landmark.ConstantCount);
        Assert.True(landmark.Density > 0);
    }
    
    [Fact]
    public void UpdateStatistics_WithNegativeCount_ThrowsArgumentException()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => landmark.UpdateStatistics(-1));
    }
    
    [Fact]
    public void UpdateStatistics_UpdatesTimestamp()
    {
        // Arrange
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        var initialTime = landmark.LastStatisticsUpdate;
        
        // Act
        Thread.Sleep(10); // Small delay
        landmark.UpdateStatistics(100);
        
        // Assert
        Assert.True(landmark.LastStatisticsUpdate > initialTime);
    }
    
    [Fact]
    public void ConstantCount_InitiallyZero()
    {
        // Arrange & Act
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10);
        
        // Assert
        Assert.Equal(0, landmark.ConstantCount);
    }
    
    #endregion
    
    #region Edge Cases and Boundary Tests
    
    [Fact]
    public void Landmark_AtLevel1_IsCoarsest()
    {
        // Arrange - level 1 is minimum valid level
        var (high, low) = HilbertCurve4D.Encode(10000, 10000, 10000, 10000);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 1);
        var landmark = Landmark.Create(prefixH, prefixL, 1, "Coarsest");
        
        // Act - check if various coordinates are contained
        var coord1 = SpatialCoordinate.FromUniversalProperties(10000, 10000, 10000, 10000);
        var coord2 = SpatialCoordinate.FromUniversalProperties(50000, 50000, 50000, 50000);
        
        // Assert - level 1 tile has very large radius
        Assert.Equal(1, landmark.Level);
        Assert.True(landmark.Radius > 1000000);
    }
    
    [Fact]
    public void Landmark_AtMaxLevel_IsFinestGranularity()
    {
        // Arrange
        var coord = SpatialCoordinate.FromUniversalProperties(12345, 67890, 11111, 22222);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(
            coord.HilbertHigh, coord.HilbertLow, level: 21);
        var landmark = Landmark.Create(prefixH, prefixL, 21, "Finest");
        
        // Act - should match original coordinate exactly
        bool contains = landmark.ContainsPoint(coord);
        
        // Assert
        Assert.True(contains);
        Assert.Equal(21, landmark.Level);
        Assert.Equal(1, landmark.Radius); // Smallest possible tile (2^(21-21) = 2^0 = 1)
    }
    
    [Fact]
    public void Landmark_WithMaxUIntCoordinates_HandlesCorrectly()
    {
        // Arrange
        uint maxCoord = (1u << 21) - 1; // 2,097,151
        var (high, low) = HilbertCurve4D.Encode(maxCoord, maxCoord, maxCoord, maxCoord);
        var (prefixH, prefixL) = HilbertCurve4D.GetHilbertTileId(high, low, level: 10);
        var landmark = Landmark.Create(prefixH, prefixL, 10, "MaxBoundary");
        
        // Act
        bool contains = landmark.ContainsHilbertIndex((high, low));
        
        // Assert
        Assert.True(contains);
    }
    
    #endregion
}
