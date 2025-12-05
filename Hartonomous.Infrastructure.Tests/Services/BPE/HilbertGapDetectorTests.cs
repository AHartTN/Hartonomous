using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Infrastructure.Services.BPE;
using Xunit;

namespace Hartonomous.Infrastructure.Tests.Services.BPE;

public class HilbertGapDetectorTests
{
    [Fact]
    public void DetectGaps_NoGaps_ReturnsEmpty()
    {
        // Arrange: Constants with sequential Hilbert indices
        var constants = CreateConstants(new ulong[] { 100, 101, 102, 103, 104 });

        // Act
        var gaps = HilbertGapDetector.DetectGaps(constants, gapThreshold: 10);

        // Assert
        Assert.Empty(gaps);
    }

    [Fact]
    public void DetectGaps_SingleGap_ReturnsOneGap()
    {
        // Arrange: Constants with one gap
        var constants = CreateConstants(new ulong[] { 100, 200, 201, 202 });

        // Act
        var gaps = HilbertGapDetector.DetectGaps(constants, gapThreshold: 10);

        // Assert
        Assert.Single(gaps);
        Assert.Equal(0, gaps[0].StartIndex);
        Assert.Equal(1, gaps[0].EndIndex);
        Assert.Equal(100UL, gaps[0].GapSize);
    }

    [Fact]
    public void DetectGaps_MultipleGaps_ReturnsAllGaps()
    {
        // Arrange: Constants with multiple gaps
        var constants = CreateConstants(new ulong[] { 100, 200, 300, 310, 500 });

        // Act
        var gaps = HilbertGapDetector.DetectGaps(constants, gapThreshold: 50);

        // Assert
        Assert.Equal(3, gaps.Count);
        Assert.Equal(100UL, gaps[0].GapSize);
        Assert.Equal(100UL, gaps[1].GapSize);
        Assert.Equal(190UL, gaps[2].GapSize);
    }

    [Fact]
    public void DetectGaps_SparseGap_FlaggedAsSparse()
    {
        // Arrange: One normal gap and one very large (sparse) gap
        var constants = CreateConstants(new ulong[] { 100, 120, 2000 });

        // Act
        var gaps = HilbertGapDetector.DetectGaps(constants, gapThreshold: 15);

        // Assert
        Assert.Equal(2, gaps.Count);
        Assert.False(gaps[0].IsSparse); // 20 < 15*10
        Assert.True(gaps[1].IsSparse);  // 1880 > 15*10
    }

    [Fact]
    public void DetectGaps_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var constants = new List<Constant>();

        // Act
        var gaps = HilbertGapDetector.DetectGaps(constants, gapThreshold: 10);

        // Assert
        Assert.Empty(gaps);
    }

    [Fact]
    public void DetectGaps_SingleConstant_ReturnsEmpty()
    {
        // Arrange
        var constants = CreateConstants(new ulong[] { 100 });

        // Act
        var gaps = HilbertGapDetector.DetectGaps(constants, gapThreshold: 10);

        // Assert
        Assert.Empty(gaps);
    }

    [Fact]
    public void BuildSegments_NoGaps_ReturnsSingleSegment()
    {
        // Arrange
        var constants = CreateConstants(new ulong[] { 100, 101, 102, 103 });
        var gaps = new List<HilbertGap>();

        // Act
        var segments = HilbertGapDetector.BuildSegments(constants, gaps);

        // Assert
        Assert.Single(segments);
        Assert.Equal(4, segments[0].Count);
    }

    [Fact]
    public void BuildSegments_WithGaps_ReturnsMultipleSegments()
    {
        // Arrange
        var constants = CreateConstants(new ulong[] { 100, 101, 200, 201, 300, 301 });
        var gaps = new List<HilbertGap>
        {
            new() { StartIndex = 1, EndIndex = 2, GapSize = 99 },
            new() { StartIndex = 3, EndIndex = 4, GapSize = 99 }
        };

        // Act
        var segments = HilbertGapDetector.BuildSegments(constants, gaps);

        // Assert
        Assert.Equal(3, segments.Count);
        Assert.Equal(2, segments[0].Count); // [100, 101]
        Assert.Equal(2, segments[1].Count); // [200, 201]
        Assert.Equal(2, segments[2].Count); // [300, 301]
    }

    [Fact]
    public void BuildSegments_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var constants = new List<Constant>();
        var gaps = new List<HilbertGap>();

        // Act
        var segments = HilbertGapDetector.BuildSegments(constants, gaps);

        // Assert
        Assert.Empty(segments);
    }

    [Fact]
    public void BuildSegments_SingleElementSegments_Excluded()
    {
        // Arrange: Gaps that would create single-element segments
        var constants = CreateConstants(new ulong[] { 100, 200, 300, 400 });
        var gaps = new List<HilbertGap>
        {
            new() { StartIndex = 0, EndIndex = 1, GapSize = 100 },
            new() { StartIndex = 1, EndIndex = 2, GapSize = 100 },
            new() { StartIndex = 2, EndIndex = 3, GapSize = 100 }
        };

        // Act
        var segments = HilbertGapDetector.BuildSegments(constants, gaps);

        // Assert: Single-element segments should be excluded
        Assert.Empty(segments);
    }

    private static List<Constant> CreateConstants(ulong[] hilbertHighValues)
    {
        var constants = new List<Constant>();

        for (int i = 0; i < hilbertHighValues.Length; i++)
        {
            var data = new byte[] { (byte)i };
            
            // Create constant (will compute hash automatically)
            var constant = Constant.Create(
                data: data,
                contentType: ContentType.Binary);

            // Override the coordinate for testing
            var coordinate = SpatialCoordinate.FromHilbert4D(
                hilbertHigh: hilbertHighValues[i],
                hilbertLow: 0,
                quantizedEntropy: 100,
                quantizedCompressibility: 100,
                quantizedConnectivity: 100,
                precision: 21);

            // Use reflection to set the private coordinate property for testing
            var coordinateProp = typeof(Constant).GetProperty("Coordinate")!;
            coordinateProp.SetValue(constant, coordinate);

            constants.Add(constant);
        }

        return constants;
    }
}
