namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Clear the terminal screen.
/// </summary>
public sealed class ClearCommand : IReplCommand
{
    public string Name => "clear";
    public IReadOnlyList<string> Aliases => ["cls"];
    public string Description => "Clear the terminal screen.";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        try
        {
            Console.Clear();
        }
        catch
        {
            // Console.Clear() may throw on some terminals
            context.Output.WriteLine("\n\n\n\n\n\n\n\n\n\n");
        }
    }
}
