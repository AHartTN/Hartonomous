using Hartonomous.Core.Entities;

namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Map a range of codepoints.
/// </summary>
public sealed class RangeCommand : IReplCommand
{
    public string Name => "range";
    public IReadOnlyList<string> Aliases => ["r"];
    public string Description => "Map a range of codepoints (max 100).";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        if (args.Length < 2)
        {
            context.Error.WriteLine("Usage: range <start> <end>");
            context.Error.WriteLine("Example: range 65 90");
            return;
        }

        if (!CodepointParser.TryParse(args[0], out var start) ||
            !CodepointParser.TryParse(args[1], out var end))
        {
            context.Error.WriteLine("Invalid codepoint range.");
            return;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (end - start > 100)
        {
            context.Error.WriteLine($"Range too large ({end - start + 1} codepoints). Maximum is 100.");
            return;
        }

        var atoms = Atom.CreateRange(start, end);
        
        // Batch output to reduce I/O overhead
        var output = new System.Text.StringBuilder(atoms.Count * 60);
        foreach (var atom in atoms)
        {
            output.Append("U+").Append(atom.Codepoint.ToString("X4")).Append(" '")
                  .Append((atom.Character ?? "N/A").PadLeft(4)).Append("' -> ")
                  .AppendLine(atom.HilbertIndex.ToString());
        }
        context.Output.Write(output);

        context.Output.WriteLine();
        context.Output.WriteLine($"Total: {atoms.Count} codepoints");
    }
}
