using Hartonomous.Core.Services;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Command to convert between Hilbert indices and 4D coordinates.
/// </summary>
public sealed class ConvertCommand : ICommand
{
    public string Name => "convert";

    public string Description => "Convert between Hilbert indices and 4D coordinates.";

    public string Usage => """
        Usage: hartonomous convert <direction> <values>

        Directions:
          to-hilbert <x> <y> <z> <w>      Convert 4D coordinates to Hilbert index
          to-coords <high> <low>          Convert Hilbert index to 4D coordinates

        Examples:
          hartonomous convert to-hilbert 100 200 300 400
          hartonomous convert to-coords 0x0 0x12345678
        """;

    public int Execute(ReadOnlySpan<string> args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Error: Missing arguments.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var direction = args[0];
        var values = args[1..];

        return direction.ToLowerInvariant() switch
        {
            "to-hilbert" => ToHilbert(values),
            "to-coords" => ToCoords(values),
            _ => UnknownDirection(direction)
        };
    }

    private static int ToHilbert(ReadOnlySpan<string> args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Error: to-hilbert requires 4 coordinate values (x, y, z, w).");
            return 1;
        }

        if (!TryParseUInt(args[0], out var x) ||
            !TryParseUInt(args[1], out var y) ||
            !TryParseUInt(args[2], out var z) ||
            !TryParseUInt(args[3], out var w))
        {
            Console.Error.WriteLine("Error: Invalid coordinate value. Must be non-negative integers.");
            return 1;
        }

        var result = HilbertCurveService.ConvertCoordinatesToIndex(x, y, z, w);
        if (result is null)
        {
            Console.Error.WriteLine("Error: Failed to compute Hilbert index.");
            return 1;
        }

        Console.WriteLine($"Coordinates: ({x}, {y}, {z}, {w})");
        Console.WriteLine($"Hilbert Index: {result.Value}");
        Console.WriteLine($"  High: {result.Value.High} (0x{result.Value.High:X16})");
        Console.WriteLine($"  Low:  {result.Value.Low} (0x{result.Value.Low:X16})");
        return 0;
    }

    private static int ToCoords(ReadOnlySpan<string> args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Error: to-coords requires 2 values (high, low).");
            return 1;
        }

        if (!TryParseLong(args[0], out var high) ||
            !TryParseLong(args[1], out var low))
        {
            Console.Error.WriteLine("Error: Invalid Hilbert index value.");
            return 1;
        }

        var result = HilbertCurveService.ConvertIndexToCoordinates(high, low);
        if (result is null)
        {
            Console.Error.WriteLine("Error: Failed to compute coordinates.");
            return 1;
        }

        Console.WriteLine($"Hilbert Index: 0x{unchecked((ulong)high):X16}{unchecked((ulong)low):X16}");
        Console.WriteLine($"Coordinates: ({result.Value.X}, {result.Value.Y}, {result.Value.Z}, {result.Value.W})");
        return 0;
    }

    private static int UnknownDirection(string direction)
    {
        Console.Error.WriteLine($"Error: Unknown direction '{direction}'.");
        Console.Error.WriteLine("Use 'to-hilbert' or 'to-coords'.");
        return 1;
    }

    private static bool TryParseUInt(string input, out uint value)
    {
        value = 0;
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        }
        return uint.TryParse(input, out value);
    }

    private static bool TryParseLong(string input, out long value)
    {
        value = 0;
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out var uval))
            {
                value = unchecked((long)uval);
                return true;
            }
            return false;
        }
        return long.TryParse(input, out value);
    }
}
