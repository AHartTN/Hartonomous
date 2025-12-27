namespace Hartonomous.CLI.Commands;

/// <summary>
/// Interface for CLI commands implementing the Command pattern.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// The primary name used to invoke this command.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Short description displayed in help text.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Detailed usage information including argument descriptions.
    /// </summary>
    string Usage { get; }

    /// <summary>
    /// Execute the command with the provided arguments.
    /// </summary>
    /// <param name="args">Command arguments (excluding the command name).</param>
    /// <returns>Exit code (0 for success, non-zero for failure).</returns>
    int Execute(ReadOnlySpan<string> args);
}
