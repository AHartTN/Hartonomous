namespace Hartonomous.CLI.Commands;

/// <summary>
/// Command to display help information about available commands.
/// </summary>
public sealed class HelpCommand : ICommand
{
    private readonly CommandRegistry _registry;

    public HelpCommand(CommandRegistry registry)
    {
        _registry = registry;
    }

    public string Name => "help";

    public string Description => "Display help information about commands.";

    public string Usage => """
        Usage: hartonomous help [command]

        Without arguments, lists all available commands.
        With a command name, shows detailed help for that command.
        """;

    public int Execute(ReadOnlySpan<string> args)
    {
        if (args.Length > 0)
        {
            return ShowCommandHelp(args[0]);
        }

        return ShowAllCommands();
    }

    private int ShowCommandHelp(string commandName)
    {
        if (_registry.TryGetCommand(commandName, out var command) && command is not null)
        {
            Console.WriteLine(command.Usage);
            return 0;
        }

        Console.Error.WriteLine($"Error: Unknown command '{commandName}'.");
        Console.Error.WriteLine("Use 'hartonomous help' to see available commands.");
        return 1;
    }

    private int ShowAllCommands()
    {
        Console.WriteLine("Hartonomous CLI - Unicode to 4D Tesseract Mapping");
        Console.WriteLine();
        Console.WriteLine("Usage: hartonomous <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");

        var maxNameLength = _registry.GetAll().Max(c => c.Name.Length);
        foreach (var command in _registry.GetAll().OrderBy(c => c.Name))
        {
            Console.WriteLine($"  {command.Name.PadRight(maxNameLength + 2)} {command.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("Use 'hartonomous help <command>' for more information about a command.");
        return 0;
    }
}
