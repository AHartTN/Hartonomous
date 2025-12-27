namespace Hartonomous.Terminal.Repl;

/// <summary>
/// Interface for REPL commands.
/// </summary>
public interface IReplCommand
{
    /// <summary>
    /// The primary name used to invoke this command.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Alternative names/shortcuts for this command.
    /// </summary>
    IReadOnlyList<string> Aliases => [];

    /// <summary>
    /// Short description displayed in help text.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Execute the command.
    /// </summary>
    /// <param name="args">Command arguments.</param>
    /// <param name="context">REPL execution context.</param>
    void Execute(ReadOnlySpan<string> args, ReplContext context);
}
