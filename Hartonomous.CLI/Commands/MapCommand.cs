using Hartonomous.Core.Entities;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Command to map Unicode codepoints to their tesseract coordinates and Hilbert indices.
/// </summary>
public sealed class MapCommand : ICommand
{
    public string Name => "map";

    public string Description => "Map Unicode codepoints to tesseract coordinates and Hilbert indices.";

    public string Usage => """
        Usage: hartonomous map <codepoint|character|range>

        Arguments:
          codepoint   A hex codepoint (e.g., U+0041, 0x41, or 41)
          character   A single character (e.g., A)
          range       A range of codepoints (e.g., U+0041-U+005A or 0-127)

        Examples:
          hartonomous map A
          hartonomous map U+0041
          hartonomous map 0x41
          hartonomous map 65
          hartonomous map 0-127
          hartonomous map U+0041-U+005A
        """;

    public int Execute(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing codepoint argument.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var input = args[0];

        if (TryParseRange(input, out var start, out var end))
        {
            return MapRange(start, end);
        }

        if (TryParseCodepoint(input, out var codepoint))
        {
            return MapSingle(codepoint);
        }

        Console.Error.WriteLine($"Error: Invalid input '{input}'.");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private static int MapSingle(int codepoint)
    {
        try
        {
            var atom = Atom.Create(codepoint);
            PrintAtom(atom);
            return 0;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int MapRange(int start, int end)
    {
        if (start > end)
        {
            Console.Error.WriteLine($"Error: Invalid range {start}-{end}. Start must be <= end.");
            return 1;
        }

        if (end - start > 10000)
        {
            Console.Error.WriteLine($"Error: Range too large ({end - start + 1} codepoints). Maximum is 10000.");
            return 1;
        }

        var atoms = Atom.CreateRange(start, end);
        
        // Batch output to reduce console I/O overhead
        var output = new System.Text.StringBuilder(atoms.Count * 200);
        foreach (var atom in atoms)
        {
            FormatAtom(atom, output);
        }
        Console.Write(output);

        Console.WriteLine($"\nTotal: {atoms.Count} codepoints mapped.");
        return 0;
    }

    private static void PrintAtom(Atom atom)
    {
        Console.WriteLine($"U+{atom.Codepoint:X4} '{atom.Character ?? "N/A"}':");
        Console.WriteLine($"  Surface: ({atom.SurfacePoint.X}, {atom.SurfacePoint.Y}, {atom.SurfacePoint.Z}, {atom.SurfacePoint.W}) Face={atom.SurfacePoint.Face}");
        Console.WriteLine($"  Hilbert: {atom.HilbertIndex}");
    }

    private static void FormatAtom(Atom atom, System.Text.StringBuilder sb)
    {
        sb.Append("U+").Append(atom.Codepoint.ToString("X4")).Append(" '").Append(atom.Character ?? "N/A").AppendLine("':");
        sb.Append("  Surface: (").Append(atom.SurfacePoint.X).Append(", ").Append(atom.SurfacePoint.Y).Append(", ")
          .Append(atom.SurfacePoint.Z).Append(", ").Append(atom.SurfacePoint.W).Append(") Face=").AppendLine(atom.SurfacePoint.Face.ToString());
        sb.Append("  Hilbert: ").AppendLine(atom.HilbertIndex.ToString());
    }

    private static bool TryParseRange(string input, out int start, out int end)
    {
        start = end = 0;

        var dashIndex = input.IndexOf('-', 1); // Skip first char to handle negative numbers
        if (dashIndex < 0)
            return false;

        var startPart = input[..dashIndex];
        var endPart = input[(dashIndex + 1)..];

        return TryParseCodepoint(startPart, out start) && TryParseCodepoint(endPart, out end);
    }

    private static bool TryParseCodepoint(string input, out int codepoint)
    {
        codepoint = 0;

        // Single character
        if (input.Length == 1)
        {
            codepoint = input[0];
            return true;
        }

        // U+XXXX format
        if (input.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("u+", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out codepoint);
        }

        // 0xXXXX format
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out codepoint);
        }

        // Plain decimal
        return int.TryParse(input, out codepoint);
    }
}
