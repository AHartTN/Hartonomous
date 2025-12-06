using Xunit;
using Xunit.Abstractions;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Data.Tests.Repositories;

public class HilbertDistanceCalculation
{
    private readonly ITestOutputHelper _output;

    public HilbertDistanceCalculation(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Calculate_Hilbert_Distance_For_Test_Coordinates()
    {
        // Test coordinates from failing test
        var center = SpatialCoordinate.FromUniversalProperties(0, 500000, 750000, 100, 21);
        var nearby = SpatialCoordinate.FromUniversalProperties(0, 510000, 760000, 150, 21);

        _output.WriteLine($"Center: X={center.X}, Y={center.Y}, Z={center.Z}, M={center.M}");
        _output.WriteLine($"Nearby: X={nearby.X}, Y={nearby.Y}, Z={nearby.Z}, M={nearby.M}");
        _output.WriteLine("");

        _output.WriteLine($"Center Hilbert: High=0x{center.HilbertHigh:X}, Low=0x{center.HilbertLow:X}");
        _output.WriteLine($"Nearby Hilbert: High=0x{nearby.HilbertHigh:X}, Low=0x{nearby.HilbertLow:X}");
        _output.WriteLine("");

        var hilbertDist = center.HilbertDistanceTo(nearby);
        var threshold = (ulong)(100000 * 1000);
        
        _output.WriteLine($"Hilbert Distance: {hilbertDist:N0}");
        _output.WriteLine($"Threshold (100K * 1000): {threshold:N0}");
        _output.WriteLine($"Passes filter: {hilbertDist <= threshold}");
        _output.WriteLine("");

        var euclidean = center.DistanceTo(nearby);
        _output.WriteLine($"Euclidean Distance: {euclidean:F2}");
        _output.WriteLine($"Test Radius: 100,000");
        _output.WriteLine($"Should pass Euclidean filter: {euclidean <= 100000}");
    }
}
