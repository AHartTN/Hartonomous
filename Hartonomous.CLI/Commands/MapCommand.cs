using Hartonomous.Commands;

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
        var output = ConsoleCommandOutput.Instance;

        if (CodepointParser.TryParseRange(input, out var start, out var end))
        {
            return MapCommandHandler.MapRange(start, end, output);
        }

        if (CodepointParser.TryParse(input, out var codepoint))
        {
            return MapCommandHandler.MapSingle(codepoint, output);
        }

        Console.Error.WriteLine($"Error: Invalid input '{input}'.");
        Console.Error.WriteLine(Usage);
        return 1;
    }
}

