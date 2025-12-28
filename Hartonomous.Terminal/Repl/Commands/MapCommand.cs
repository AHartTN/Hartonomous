using Hartonomous.Commands;

namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Map a codepoint or character to its tesseract representation.
/// </summary>
public sealed class MapCommand : IReplCommand
{
    public string Name => "map";
    public IReadOnlyList<string> Aliases => ["m"];
    public string Description => "Map a codepoint to its tesseract coordinates.";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        if (args.Length == 0)
        {
            context.Error.WriteLine("Usage: map <codepoint|character>");
            return;
        }

        var input = args[0];
        var output = new TextWriterCommandOutput(context.Output, context.Error);

        if (!CodepointParser.TryParse(input, out var codepoint))
        {
            context.Error.WriteLine($"Invalid input: {input}");
            return;
        }

        MapCommandHandler.MapSingle(codepoint, output);
    }
}
