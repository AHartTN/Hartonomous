using System;
using Hartonomous.Core.Domain.Utilities;

namespace Hartonomous.NativeDebug;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Debugging Native Interop...");

        try 
        {
            // Test Case 1: Simple Value
            uint x = 100, y = 200, z = 300, m = 400;
            int precision = 21;

            Console.WriteLine($"Encoding: {x}, {y}, {z}, {m} (p={precision})");
            var (high, low) = HilbertCurve4D.Encode(x, y, z, m, precision);
            Console.WriteLine($"Encoded: H={high}, L={low}");

            var decoded = HilbertCurve4D.Decode(high, low, precision);
            Console.WriteLine($"Decoded: {decoded.X}, {decoded.Y}, {decoded.Z}, {decoded.M}");

            if (decoded.X == x && decoded.Y == y && decoded.Z == z && decoded.M == m)
            {
                Console.WriteLine("SUCCESS: Round trip match.");
            }
            else
            {
                Console.WriteLine("FAILURE: Round trip mismatch.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CRITICAL ERROR: {ex}");
        }
    }
}