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

        try
        {
            return subcommand switch
            {
                "stats" => QueryStats(),
                "exists" when args.Length > 1 => QueryExists(args[1]),
                "exists" => Error("Error: Text argument required for 'exists' subcommand."),
                "encode" when args.Length > 1 => EncodeContent(args[1]),
                "encode" => Error("Error: Text argument required for 'encode' subcommand."),
                "decode" when args.Length > 1 => DecodeContent(args[1]),
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

    private static int QueryStats()
    {
        var db = DatabaseService.Instance;
        var stats = db.GetStats();

        Console.WriteLine("Hartonomous Substrate Statistics");
        Console.WriteLine("================================");
        Console.WriteLine($"{"Atoms:",-20} {stats.AtomCount:N0}");
        Console.WriteLine($"{"Compositions:",-20} {stats.CompositionCount:N0}");
        Console.WriteLine($"{"Relationships:",-20} {stats.RelationshipCount:N0}");
        if (stats.DatabaseSizeBytes > 0)
        {
            Console.WriteLine($"{"Database Size:",-20} {FormatBytes(stats.DatabaseSizeBytes)}");
        }

        return 0;
    }

    private static int QueryExists(string text)
    {
        var db = DatabaseService.Instance;
        var exists = db.ContentExists(text);

        Console.WriteLine($"Content: \"{text}\"");
        Console.WriteLine($"Exists:  {(exists ? "YES" : "NO")}");

        return exists ? 0 : 1;
    }

    private static int EncodeContent(string text)
    {
        var db = DatabaseService.Instance;
        var (high, low) = db.EncodeAndStore(text);

        Console.WriteLine($"Content: \"{text}\"");
        Console.WriteLine($"Root ID: {high}:{low}");

        return 0;
    }

    private static int DecodeContent(string idStr)
    {
        var parts = idStr.Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], out var high) ||
            !long.TryParse(parts[1], out var low))
        {
            Console.Error.WriteLine("Error: ID must be in format 'high:low' (e.g., 123456789:987654321)");
            return 1;
        }

        var db = DatabaseService.Instance;
        var text = db.Decode(high, low);

        Console.WriteLine($"Root ID: {high}:{low}");
        Console.WriteLine($"Content: \"{text}\"");
        Console.WriteLine($"Length:  {text.Length} characters");

        return 0;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
