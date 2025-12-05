// Hartonomous.Marshal.CLI - Command-line interface for testing P/Invoke wrappers
// Usage: Hartonomous.Marshal.CLI.exe {command} {params...}
//
// Available commands:
//   HilbertEncode {x} {y} {z} {m} - Encode 4D coordinates via P/Invoke
//   HilbertDecode {high64} {low64} - Decode Hilbert index via P/Invoke
//   RoundTrip {x} {y} {z} {m}      - Test encode→decode round trip
//
// Examples:
//   Hartonomous.Marshal.CLI.exe HilbertEncode 100 200 300 400
//   Hartonomous.Marshal.CLI.exe HilbertDecode 0x0 0x7045fb565
//   Hartonomous.Marshal.CLI.exe RoundTrip 100 200 300 400

using System;
using System.Runtime.InteropServices;
using HartonomousMarshal = Hartonomous.Marshal;

namespace Hartonomous.Marshal.CLI;

class Program
{
    const int PRECISION = 21; // 21-bit quantization
    const uint MAX_VALUE = (1u << PRECISION) - 1; // 2,097,151

    static void Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            string command = args[0];

            switch (command.ToLower())
            {
                case "hilbertencode":
                    ExecuteHilbertEncode(args);
                    break;
                case "hilbertdecode":
                    ExecuteHilbertDecode(args);
                    break;
                case "roundtrip":
                    ExecuteRoundTrip(args);
                    break;
                case "--help":
                case "-h":
                case "help":
                    PrintUsage();
                    break;
                default:
                    Console.Error.WriteLine($"Error: Unknown command '{command}'");
                    PrintUsage();
                    Environment.Exit(1);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Hartonomous Marshal CLI - P/Invoke Wrapper Testing");
        Console.WriteLine();
        Console.WriteLine("Usage: Hartonomous.Marshal.CLI.exe {command} {params...}");
        Console.WriteLine();
        Console.WriteLine("Available Commands:");
        Console.WriteLine("  HilbertEncode {x} {y} {z} {m}");
        Console.WriteLine("    - Encode 4D POINTZM coordinates via P/Invoke");
        Console.WriteLine("    - Parameters: x, y, z, m (0 to 2097151 for 21-bit quantization)");
        Console.WriteLine("    - Returns: high64 low64 (two 64-bit hex values)");
        Console.WriteLine();
        Console.WriteLine("  HilbertDecode {high64} {low64}");
        Console.WriteLine("    - Decode 128-bit Hilbert index via P/Invoke");
        Console.WriteLine("    - Parameters: high64, low64 (64-bit hex values with 0x prefix)");
        Console.WriteLine("    - Returns: x y z m");
        Console.WriteLine();
        Console.WriteLine("  RoundTrip {x} {y} {z} {m}");
        Console.WriteLine("    - Test encode→decode round trip via P/Invoke");
        Console.WriteLine("    - Verifies managed→native→managed data integrity");
        Console.WriteLine("    - Returns: success/failure with original vs decoded values");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  Hartonomous.Marshal.CLI.exe HilbertEncode 100 200 300 400");
        Console.WriteLine("  Hartonomous.Marshal.CLI.exe HilbertDecode 0x0 0x7045fb565");
        Console.WriteLine("  Hartonomous.Marshal.CLI.exe RoundTrip 100 200 300 400");
    }

    static void ExecuteHilbertEncode(string[] args)
    {
        if (args.Length != 5)
        {
            throw new ArgumentException("HilbertEncode requires 4 parameters: x y z m");
        }

        uint x = ParseCoordinate(args[1], "x");
        uint y = ParseCoordinate(args[2], "y");
        uint z = ParseCoordinate(args[3], "z");
        uint m = ParseCoordinate(args[4], "m");

        Console.WriteLine($"[P/Invoke Call] HilbertEncode4D(x={x}, y={y}, z={z}, m={m}, precision={PRECISION})");
        
        HartonomousMarshal.NativeMethods.HilbertEncode4D(x, y, z, m, PRECISION, out ulong high64, out ulong low64);

        Console.WriteLine($"[P/Invoke Result] Successfully encoded to Hilbert index");
        Console.WriteLine($"Hilbert Index (128-bit):");
        Console.WriteLine($"  High64: 0x{high64:X}");
        Console.WriteLine($"  Low64:  0x{low64:X}");
        Console.WriteLine();
        Console.WriteLine($"[For Decode] Use: Hartonomous.Marshal.CLI.exe HilbertDecode 0x{high64:X} 0x{low64:X}");
    }

    static void ExecuteHilbertDecode(string[] args)
    {
        if (args.Length != 3)
        {
            throw new ArgumentException("HilbertDecode requires 2 parameters: high64 low64 (hex with 0x prefix)");
        }

        ulong high64 = ParseHex(args[1], "high64");
        ulong low64 = ParseHex(args[2], "low64");

        Console.WriteLine($"[P/Invoke Call] HilbertDecode4D(high=0x{high64:X}, low=0x{low64:X}, precision={PRECISION})");

        // Allocate array for result coordinates
        uint[] coords = new uint[4];
        IntPtr coordsPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * 4);
        
        try
        {
            HartonomousMarshal.NativeMethods.HilbertDecode4D(high64, low64, PRECISION, coordsPtr);
            
            // Copy results back
            System.Runtime.InteropServices.Marshal.Copy(coordsPtr, (int[])(object)coords, 0, 4);

            Console.WriteLine($"[P/Invoke Result] Successfully decoded from Hilbert index");
            Console.WriteLine($"Decoded Coordinates:");
            Console.WriteLine($"  X: {coords[0]}");
            Console.WriteLine($"  Y: {coords[1]}");
            Console.WriteLine($"  Z: {coords[2]}");
            Console.WriteLine($"  M: {coords[3]}");
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(coordsPtr);
        }
    }

    static void ExecuteRoundTrip(string[] args)
    {
        if (args.Length != 5)
        {
            throw new ArgumentException("RoundTrip requires 4 parameters: x y z m");
        }

        uint originalX = ParseCoordinate(args[1], "x");
        uint originalY = ParseCoordinate(args[2], "y");
        uint originalZ = ParseCoordinate(args[3], "z");
        uint originalM = ParseCoordinate(args[4], "m");

        Console.WriteLine($"=== P/Invoke Round Trip Test ===");
        Console.WriteLine($"Original Coordinates: x={originalX}, y={originalY}, z={originalZ}, m={originalM}");
        Console.WriteLine();

        // Step 1: Encode
        Console.WriteLine($"[Step 1] Encoding via P/Invoke...");
        HartonomousMarshal.NativeMethods.HilbertEncode4D(originalX, originalY, originalZ, originalM, PRECISION, out ulong high64, out ulong low64);
        Console.WriteLine($"  Encoded: High=0x{high64:X}, Low=0x{low64:X}");
        Console.WriteLine();

        // Step 2: Decode
        Console.WriteLine($"[Step 2] Decoding via P/Invoke...");
        uint[] coords = new uint[4];
        IntPtr coordsPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * 4);
        
        try
        {
            HartonomousMarshal.NativeMethods.HilbertDecode4D(high64, low64, PRECISION, coordsPtr);
            System.Runtime.InteropServices.Marshal.Copy(coordsPtr, (int[])(object)coords, 0, 4);

            uint decodedX = coords[0];
            uint decodedY = coords[1];
            uint decodedZ = coords[2];
            uint decodedM = coords[3];

            Console.WriteLine($"  Decoded: x={decodedX}, y={decodedY}, z={decodedZ}, m={decodedM}");
            Console.WriteLine();

            // Step 3: Validate
            Console.WriteLine($"[Step 3] Validating round trip...");
            bool success = 
                decodedX == originalX &&
                decodedY == originalY &&
                decodedZ == originalZ &&
                decodedM == originalM;

            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ ROUND TRIP SUCCESSFUL - All coordinates match!");
                Console.ResetColor();
                Console.WriteLine($"  Managed → Native → Managed data integrity verified");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ ROUND TRIP FAILED - Coordinate mismatch detected!");
                Console.ResetColor();
                Console.WriteLine($"  X: {originalX} → {decodedX} {(decodedX == originalX ? "✓" : "✗")}");
                Console.WriteLine($"  Y: {originalY} → {decodedY} {(decodedY == originalY ? "✓" : "✗")}");
                Console.WriteLine($"  Z: {originalZ} → {decodedZ} {(decodedZ == originalZ ? "✓" : "✗")}");
                Console.WriteLine($"  M: {originalM} → {decodedM} {(decodedM == originalM ? "✓" : "✗")}");
                Environment.Exit(1);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(coordsPtr);
        }
    }

    static uint ParseCoordinate(string value, string name)
    {
        if (!uint.TryParse(value, out uint result))
        {
            throw new ArgumentException($"Invalid {name} coordinate: '{value}' (must be unsigned integer)");
        }

        if (result > MAX_VALUE)
        {
            throw new ArgumentOutOfRangeException(name, 
                $"Coordinate {name}={result} exceeds max value {MAX_VALUE} for {PRECISION}-bit quantization");
        }

        return result;
    }

    static ulong ParseHex(string value, string name)
    {
        // Remove 0x prefix if present
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(2);
        }

        if (!ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out ulong result))
        {
            throw new ArgumentException($"Invalid {name} hex value: '{value}' (must be hex with optional 0x prefix)");
        }

        return result;
    }
}
