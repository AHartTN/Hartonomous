using Hartonomous.Commands;
using Hartonomous.Core.Services;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Query the Hartonomous substrate for content, statistics, and relationships.
/// </summary>
public sealed class QueryCommand : ICommand
{
    public string Name => "query";
    public string Description => "Query the substrate for content, stats, or relationships.";

    public string Usage => """
        Usage: hartonomous query <subcommand> [arguments]

        Subcommands:
          stats           Show database statistics (atoms, compositions, relationships)
          exists <text>   Check if content exists in the substrate
          encode <text>   Encode text and store, return root ID
          decode <id>     Decode a root ID back to original content

        Examples:
          hartonomous query stats
          hartonomous query exists "Captain Ahab"
          hartonomous query encode "Hello, World!"
          hartonomous query decode 1234567890123456789:9876543210987654321
        """;

    public int Execute(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Subcommand required.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var subcommand = args[0];
        var db = DatabaseService.Instance;
        var output = ConsoleCommandOutput.Instance;

        try
        {
            return subcommand switch
            {
                "stats" => QueryCommandHandler.QueryStats(db, output),
                "exists" when args.Length > 1 => QueryCommandHandler.QueryExists(db, args[1], output),
                "exists" => Error("Error: Text argument required for 'exists' subcommand."),
                "encode" when args.Length > 1 => QueryCommandHandler.EncodeContent(db, args[1], output),
                "encode" => Error("Error: Text argument required for 'encode' subcommand."),
                "decode" when args.Length > 1 => QueryCommandHandler.DecodeContent(db, args[1], output),
                "decode" => Error("Error: ID argument required for 'decode' subcommand."),
                _ => Error($"Error: Unknown subcommand '{subcommand}'.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
