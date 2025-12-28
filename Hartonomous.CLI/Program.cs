using Hartonomous.CLI.Commands;

namespace Hartonomous.CLI;

/// <summary>
/// Hartonomous CLI entry point.
/// </summary>
internal static class Program
{
    private static CommandRegistry BuildRegistry()
    {
        // Create commands without circular dependencies first
        var commands = new List<ICommand>
        {
            new MapCommand(),
            new ConvertCommand(),
            new ValidateCommand(),
            new InfoCommand(),
            new VersionCommand(),
            new IngestCommand(),
            new QueryCommand(),
            new CompleteCommand(),
            new AskCommand()
        };

        var registry = new CommandRegistry(commands);

        // Add help command (needs registry reference)
        var allCommands = commands.Append(new HelpCommand(registry));
        return new CommandRegistry(allCommands);
    }

    public static int Main(string[] args)
    {
        var registry = BuildRegistry();

        if (args.Length == 0)
        {
            if (registry.TryGetCommand("help", out var helpCommand) && helpCommand is not null)
            {
                return helpCommand.Execute(ReadOnlySpan<string>.Empty);
            }
            return 0;
        }

        var commandName = args[0];

        if (commandName is "-h" or "--help")
        {
            if (registry.TryGetCommand("help", out var helpCommand) && helpCommand is not null)
            {
                return helpCommand.Execute(ReadOnlySpan<string>.Empty);
            }
            return 0;
        }

        if (commandName is "-v" or "--version")
        {
            if (registry.TryGetCommand("version", out var versionCommand) && versionCommand is not null)
            {
                return versionCommand.Execute(ReadOnlySpan<string>.Empty);
            }
            return 0;
        }

        if (!registry.TryGetCommand(commandName, out var command) || command is null)
        {
            Console.Error.WriteLine($"Error: Unknown command '{commandName}'.");
            Console.Error.WriteLine("Use 'hartonomous help' to see available commands.");
            return 1;
        }

        return command.Execute(args.AsSpan()[1..]);
    }
}
