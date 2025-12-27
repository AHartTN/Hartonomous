using System.Collections.Frozen;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Registry of all available CLI commands.
/// </summary>
public sealed class CommandRegistry
{
    private readonly FrozenDictionary<string, ICommand> _commands;

    public CommandRegistry(IEnumerable<ICommand> commands)
    {
        _commands = commands.ToFrozenDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Try to get a command by name.
    /// </summary>
    public bool TryGetCommand(string name, out ICommand? command) =>
        _commands.TryGetValue(name, out command);

    /// <summary>
    /// Get all registered commands.
    /// </summary>
    public IEnumerable<ICommand> GetAll() => _commands.Values;
}
