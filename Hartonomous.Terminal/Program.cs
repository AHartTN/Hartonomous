using Hartonomous.Terminal.Repl;
using Hartonomous.Terminal.Repl.Commands;

namespace Hartonomous.Terminal;

/// <summary>
/// Hartonomous Interactive Terminal entry point.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        var commands = new IReplCommand[]
        {
            new HelpCommand(),
            new MapCommand(),
            new RangeCommand(),
            new InfoCommand(),
            new ClearCommand(),
            new ExitCommand()
        };

        var registry = new ReplCommandRegistry(commands);
        var engine = new ReplEngine(registry);

        engine.Run();
    }
}
