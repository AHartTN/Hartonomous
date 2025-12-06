using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Data.IntegrationTests.Builders;

/// <summary>
/// Factory methods for creating common test data scenarios.
/// Provides high-level abstractions over builders for typical test cases.
/// </summary>
public static class TestDataFactory
{
    /// <summary>
    /// Creates a cluster of constants around a center point.
    /// Useful for testing spatial proximity queries.
    /// </summary>
    public static List<Constant> CreateSpatialCluster(
        SpatialCoordinate center, 
        int count, 
        int radius)
    {
        var constants = new List<Constant>();
        var random = new Random(42); // Deterministic seed

        for (int i = 0; i < count; i++)
        {
            // Generate random offset within radius
            var yOffset = random.Next(-radius, radius);
            var zOffset = random.Next(-radius, radius);
            var mOffset = random.Next(-radius / 10, radius / 10); // M dimension smaller variance

            var y = Math.Clamp(center.QuantizedEntropy + yOffset, 0, 2_097_151);
            var z = Math.Clamp(center.QuantizedCompressibility + zOffset, 0, 2_097_151);
            var m = Math.Clamp(center.QuantizedConnectivity + mOffset, 0, 2_097_151);

            var coord = SpatialCoordinate.FromUniversalProperties(
                (uint)i, 
                y, 
                z, 
                m);

            var constant = new ConstantBuilder()
                .WithData(new byte[] { (byte)i })
                .WithCoordinate(coord)
                .Active()
                .Build();

            constants.Add(constant);
        }

        return constants;
    }

    /// <summary>
    /// Creates constants at specific known distances from a center.
    /// Returns tuple: (center, close, medium, far)
    /// </summary>
    public static (Constant center, Constant close, Constant medium, Constant far) CreateDistanceTestSet()
    {
        var centerCoord = SpatialCoordinate.FromUniversalProperties(0, 500_000, 750_000, 100);
        var closeCoord = SpatialCoordinate.FromUniversalProperties(0, 505_000, 755_000, 110); // ~7071 distance
        var mediumCoord = SpatialCoordinate.FromUniversalProperties(0, 550_000, 800_000, 150); // ~70711 distance
        var farCoord = SpatialCoordinate.FromUniversalProperties(0, 700_000, 950_000, 500); // ~282843 distance

        var center = new ConstantBuilder()
            .WithData(new byte[] { 0x01 })
            .WithCoordinate(centerCoord)
            .Active()
            .Build();

        var close = new ConstantBuilder()
            .WithData(new byte[] { 0x02 })
            .WithCoordinate(closeCoord)
            .Active()
            .Build();

        var medium = new ConstantBuilder()
            .WithData(new byte[] { 0x03 })
            .WithCoordinate(mediumCoord)
            .Active()
            .Build();

        var far = new ConstantBuilder()
            .WithData(new byte[] { 0x04 })
            .WithCoordinate(farCoord)
            .Active()
            .Build();

        return (center, close, medium, far);
    }

    /// <summary>
    /// Creates constants with varying statuses for status filtering tests.
    /// </summary>
    public static List<Constant> CreateMixedStatusConstants(SpatialCoordinate location)
    {
        return new List<Constant>
        {
            new ConstantBuilder()
                .WithData(new byte[] { 0x01 })
                .WithCoordinate(location)
                .Active()
                .Build(),
            
            new ConstantBuilder()
                .WithData(new byte[] { 0x02 })
                .WithCoordinate(location)
                .Projected()
                .Build(),
            
            new ConstantBuilder()
                .WithData(new byte[] { 0x03 })
                .WithCoordinate(location)
                .WithStatus(ConstantStatus.Pending)
                .Build()
        };
    }

    /// <summary>
    /// Creates constants with high reference counts for frequency testing.
    /// </summary>
    public static List<Constant> CreateFrequencyTestSet()
    {
        return new List<Constant>
        {
            new ConstantBuilder()
                .WithData(new byte[] { 0x01 })
                .WithFrequency(1000)
                .Active()
                .Build(),
            
            new ConstantBuilder()
                .WithData(new byte[] { 0x02 })
                .WithFrequency(500)
                .Active()
                .Build(),
            
            new ConstantBuilder()
                .WithData(new byte[] { 0x03 })
                .WithFrequency(100)
                .Active()
                .Build(),
            
            new ConstantBuilder()
                .WithData(new byte[] { 0x04 })
                .WithFrequency(10)
                .Active()
                .Build()
        };
    }
}
