namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Exit the REPL.
/// </summary>
public sealed class ExitCommand : IReplCommand
{
    public string Name => "exit";
    public IReadOnlyList<string> Aliases => ["quit", "q"];
    public string Description => "Exit the terminal.";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        context.Output.WriteLine("Goodbye!");
        context.ShouldExit = true;
    }
}
