using System.Collections.Frozen;

namespace Hartonomous.Terminal.Repl;

/// <summary>
/// Registry of all available REPL commands.
/// </summary>
public sealed class ReplCommandRegistry
{
    private readonly FrozenDictionary<string, IReplCommand> _commands;
    private readonly IReadOnlyList<IReplCommand> _allCommands;

    public ReplCommandRegistry(IEnumerable<IReplCommand> commands)
    {
        var commandArray = commands as IReplCommand[] ?? commands.ToArray();
        _allCommands = commandArray;

        // Flatten commands and aliases in parallel, then build frozen dictionary
        var entries = commandArray
            .AsParallel()
            .SelectMany(cmd => cmd.Aliases
                .Select(alias => KeyValuePair.Create(alias, cmd))
                .Prepend(KeyValuePair.Create(cmd.Name, cmd)))
            .ToArray();

        _commands = entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Try to get a command by name or alias.
    /// </summary>
    public bool TryGetCommand(string name, out IReplCommand? command) =>
        _commands.TryGetValue(name, out command);

    /// <summary>
    /// Get all unique commands (no aliases).
    /// </summary>
    public IEnumerable<IReplCommand> GetAll() => _allCommands;
}
