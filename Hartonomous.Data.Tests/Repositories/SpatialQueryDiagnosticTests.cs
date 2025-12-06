// Hartonomous.Data.Tests/SpatialQueryDiagnosticTests.cs
using Xunit;
using Xunit.Abstractions;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Core.Domain.Utilities;
using System.Threading.Tasks;

namespace Hartonomous.Data.Tests.Repositories
{
    /// <summary>
    /// Diagnostic tests to verify the Hilbert range calculation overflow fix.
    /// Tests the specific scenario from failing tests: radius 10B with coordinates.
    /// </summary>
    public class SpatialQueryDiagnosticTests
    {
        private readonly ITestOutputHelper _output;

        public SpatialQueryDiagnosticTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Diagnose_Coordinates_And_Radius()
        {
            _output.WriteLine("=== DIAGNOSTIC START ===");

            // 1. Recreate the specific scenario
            // Center: (0, 500K, 750K, 100)
            // Nearby: (0, 510K, 760K, 150)
            var center = SpatialCoordinate.FromUniversalProperties(0, 500_000, 750_000, 100, 21);
            var nearby = SpatialCoordinate.FromUniversalProperties(0, 510_000, 760_000, 150, 21);

            _output.WriteLine($"Center Index: H={center.HilbertHigh} L={center.HilbertLow}");
            _output.WriteLine($"Nearby Index: H={nearby.HilbertHigh} L={nearby.HilbertLow}");

            // 2. Check Distance
            var dist = center.DistanceTo(nearby);
            _output.WriteLine($"Euclidean Distance: {dist}");

            // 3. Check Range Calculation for Massive Radius
            double largeRadius = 10_000_000_000; // 10 Billion
            
            // This call previously overflowed internally
            var range = HilbertCurve4D.GetRangeForRadius(center.HilbertHigh, center.HilbertLow, largeRadius);
            
            _output.WriteLine($"Query Range for 10B Radius:");
            _output.WriteLine($"Min: H={range.MinHigh} L={range.MinLow}");
            _output.WriteLine($"Max: H={range.MaxHigh} L={range.MaxLow}");

            // 4. Verify Inclusion
            bool isIncluded = IsIndexInRange(nearby, range);
            _output.WriteLine($"Is 'Nearby' item included in range? {isIncluded}");

            Assert.True(isIncluded, "The nearby item MUST be included in the Hilbert range for Infinite Radius.");
            
            await Task.CompletedTask; // Make it async to match signature
        }

        private bool IsIndexInRange(SpatialCoordinate c, (ulong MinHigh, ulong MinLow, ulong MaxHigh, ulong MaxLow) range)
        {
            // 84-bit comparison logic
            bool gteMin = (c.HilbertHigh > range.MinHigh) || (c.HilbertHigh == range.MinHigh && c.HilbertLow >= range.MinLow);
            bool lteMax = (c.HilbertHigh < range.MaxHigh) || (c.HilbertHigh == range.MaxHigh && c.HilbertLow <= range.MaxLow);
            return gteMin && lteMax;
        }
    }
}
