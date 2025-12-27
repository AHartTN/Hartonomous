namespace Hartonomous.Terminal.Repl;

/// <summary>
/// Execution context for REPL commands.
/// </summary>
public sealed class ReplContext
{
    /// <summary>
    /// Flag to indicate the REPL should exit.
    /// </summary>
    public bool ShouldExit { get; set; }

    /// <summary>
    /// The command registry for looking up commands.
    /// </summary>
    public required ReplCommandRegistry Registry { get; init; }

    /// <summary>
    /// Output writer (defaults to Console.Out).
    /// </summary>
    public TextWriter Output { get; init; } = Console.Out;

    /// <summary>
    /// Error writer (defaults to Console.Error).
    /// </summary>
    public TextWriter Error { get; init; } = Console.Error;
}
