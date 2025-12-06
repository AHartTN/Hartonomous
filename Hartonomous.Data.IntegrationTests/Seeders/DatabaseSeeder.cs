using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Data.Context;

namespace Hartonomous.Data.IntegrationTests;

/// <summary>
/// Seeds PostgreSQL database with realistic test data for integration tests.
/// Creates constants at known spatial positions for predictable spatial query results.
/// </summary>
public class DatabaseSeeder
{
    private readonly ApplicationDbContext _context;

    public DatabaseSeeder(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        // Clear existing data
        _context.Constants.RemoveRange(_context.Constants);
        _context.Landmarks.RemoveRange(_context.Landmarks);
        await _context.SaveChangesAsync();

        // Seed spatial grid of constants
        await SeedSpatialGridAsync();
        
        // Seed landmark tiles
        await SeedLandmarksAsync();
    }

    /// <summary>
    /// Seeds a 5x5x5 grid of constants in YZM space for spatial query testing.
    /// Grid spans: Y[250k-1750k], Z[250k-1750k], M[0-2000] with 500k spacing.
    /// Total: 125 constants at predictable positions.
    /// </summary>
    private async Task SeedSpatialGridAsync()
    {
        var constants = new List<Constant>();
        int id = 0;

        // Create 5x5x5 grid (Y, Z, M dimensions)
        for (int y = 0; y < 5; y++)
        {
            for (int z = 0; z < 5; z++)
            {
                for (int m = 0; m < 5; m++)
                {
                    id++;
                    var yVal = 250_000 + (y * 500_000); // 250k, 750k, 1.25M, 1.75M, 2.1M (capped at max)
                    var zVal = 250_000 + (z * 500_000);
                    var mVal = m * 500;

                    // Clamp to valid 21-bit range [0, 2,097,151]
                    yVal = Math.Min(yVal, 2_097_151);
                    zVal = Math.Min(zVal, 2_097_151);
                    mVal = Math.Min(mVal, 2_097_151);

                    var coord = SpatialCoordinate.FromUniversalProperties(
                        (uint)id, 
                        yVal, 
                        zVal, 
                        mVal);

                    var data = new byte[] { (byte)(id % 256), (byte)((id / 256) % 256) };
                    var constant = Constant.Create(data, ContentType.Binary);
                    constant.SetCoordinateForTesting(coord);
                    constant.ActivateForTesting();

                    constants.Add(constant);
                }
            }
        }

        await _context.Constants.AddRangeAsync(constants);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds landmark tiles at multiple Hilbert precision levels.
    /// Creates deterministic spatial partitioning for landmark tests.
    /// </summary>
    private async Task SeedLandmarksAsync()
    {
        var landmarks = new List<Landmark>();

        // Create landmarks at levels 10, 15, 20 (coarse to fine)
        var levels = new[] { 10, 15, 20 };
        var tileIds = new[]
        {
            (High: 0UL, Low: 0UL),
            (High: 100_000UL, Low: 50_000UL),
            (High: 500_000UL, Low: 250_000UL),
            (High: 1_000_000UL, Low: 500_000UL)
        };

        int landmarkId = 1;
        foreach (var level in levels)
        {
            foreach (var (high, low) in tileIds)
            {
                // Landmark.Create signature: (hilbertPrefixHigh, hilbertPrefixLow, level, description)
                var landmark = Landmark.Create(
                    hilbertPrefixHigh: high,
                    hilbertPrefixLow: low,
                    level: level,
                    description: $"Test landmark L{level}_T{landmarkId} at level {level}");

                landmarks.Add(landmark);
                landmarkId++;
            }
        }

        await _context.Landmarks.AddRangeAsync(landmarks);
        await _context.SaveChangesAsync();
    }
}
