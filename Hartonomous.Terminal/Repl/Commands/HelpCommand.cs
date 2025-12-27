namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Display help information.
/// </summary>
public sealed class HelpCommand : IReplCommand
{
    public string Name => "help";
    public IReadOnlyList<string> Aliases => ["?", "h"];
    public string Description => "Display help information.";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        context.Output.WriteLine("Available commands:");
        context.Output.WriteLine();

        var maxNameLength = context.Registry.GetAll().Max(c => c.Name.Length);
        foreach (var command in context.Registry.GetAll().OrderBy(c => c.Name))
        {
            var aliases = command.Aliases.Count > 0
                ? $" ({string.Join(", ", command.Aliases)})"
                : "";
            context.Output.WriteLine($"  {command.Name.PadRight(maxNameLength + 2)} {command.Description}{aliases}");
        }

        context.Output.WriteLine();
        context.Output.WriteLine("You can also enter a single character or codepoint directly.");
        context.Output.WriteLine("Examples: A, U+0041, 0x41, 65");
    }
}
