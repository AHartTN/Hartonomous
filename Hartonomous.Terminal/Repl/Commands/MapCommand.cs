using Hartonomous.Core.Entities;

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
        if (!CodepointParser.TryParse(input, out var codepoint))
        {
            context.Error.WriteLine($"Invalid input: {input}");
            return;
        }

        try
        {
            var atom = Atom.Create(codepoint);
            PrintAtom(atom, context.Output);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            context.Error.WriteLine($"Error: {ex.Message}");
        }
    }

    internal static void PrintAtom(Atom atom, TextWriter output)
    {
        output.WriteLine($"Codepoint: U+{atom.Codepoint:X4} ({atom.Codepoint})");
        output.WriteLine($"Character: '{atom.Character ?? "N/A"}'");
        output.WriteLine($"Surface:   ({atom.SurfacePoint.X}, {atom.SurfacePoint.Y}, {atom.SurfacePoint.Z}, {atom.SurfacePoint.W})");
        output.WriteLine($"Face:      {atom.SurfacePoint.Face}");
        output.WriteLine($"Hilbert:   {atom.HilbertIndex}");
    }
}
