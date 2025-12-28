using Hartonomous.Commands;

namespace Hartonomous.Terminal.Repl;

/// <summary>
/// Main REPL (Read-Eval-Print-Loop) engine.
/// </summary>
public sealed class ReplEngine
{
    private readonly ReplContext _context;

    public ReplEngine(ReplCommandRegistry registry)
    {
        _context = new ReplContext { Registry = registry };
    }

    /// <summary>
    /// Run the REPL until exit is requested.
    /// </summary>
    public void Run()
    {
        PrintBanner();

        while (!_context.ShouldExit)
        {
            Console.Write("hartonomous> ");
            var line = Console.ReadLine();

            if (line is null)
            {
                // EOF (Ctrl+D or Ctrl+Z)
                _context.ShouldExit = true;
                continue;
            }

            ProcessLine(line);
        }
    }

    private static void PrintBanner()
    {
        Console.WriteLine("╔═════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              Hartonomous Interactive Terminal               ║");
        Console.WriteLine("║         Unicode to 4D Tesseract Mapping Explorer            ║");
        Console.WriteLine("╚═════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Type 'help' for available commands, or enter a codepoint directly.");
        Console.WriteLine();
    }

    private void ProcessLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        var commandName = parts[0];
        var args = parts.AsSpan()[1..];

        // Try to find a command
        if (_context.Registry.TryGetCommand(commandName, out var command) && command is not null)
        {
            command.Execute(args, _context);
            Console.WriteLine();
            return;
        }

        // If not a command, try to interpret as a codepoint directly
        if (CodepointParser.TryParse(trimmed, out var codepoint))
        {
            TryMapCodepoint(codepoint);
            return;
        }

        Console.Error.WriteLine($"Unknown command: {commandName}");
        Console.Error.WriteLine("Type 'help' for available commands.");
        Console.WriteLine();
    }

    private static void TryMapCodepoint(int codepoint)
    {
        var output = ConsoleCommandOutput.Instance;
        MapCommandHandler.MapSingle(codepoint, output);
        Console.WriteLine();
    }
}
