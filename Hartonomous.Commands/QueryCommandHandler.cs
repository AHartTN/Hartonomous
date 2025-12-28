using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Commands;

/// <summary>
/// Shared logic for the 'query' command.
/// </summary>
public static class QueryCommandHandler
{
    /// <summary>
    /// Show database statistics.
    /// </summary>
    public static int QueryStats(IDatabaseService db, ICommandOutput output)
    {
        try
        {
            db.Initialize();
            var stats = db.GetStats();

            output.WriteLine("Hartonomous Substrate Statistics");
            output.WriteLine("================================");
            output.WriteLine($"{"Atoms:",-20} {stats.AtomCount:N0}");
            output.WriteLine($"{"Compositions:",-20} {stats.CompositionCount:N0}");
            output.WriteLine($"{"Relationships:",-20} {stats.RelationshipCount:N0}");
            if (stats.DatabaseSizeBytes > 0)
            {
                output.WriteLine($"{"Database Size:",-20} {FormatBytes(stats.DatabaseSizeBytes)}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Check if content exists.
    /// </summary>
    public static int QueryExists(IDatabaseService db, string text, ICommandOutput output)
    {
        try
        {
            db.Initialize();
            var exists = db.ContentExists(text);

            output.WriteLine($"Content: \"{text}\"");
            output.WriteLine($"Exists:  {(exists ? "YES" : "NO")}");

            return exists ? 0 : 1;
        }
        catch (Exception ex)
        {
            output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Encode and store content.
    /// </summary>
    public static int EncodeContent(IDatabaseService db, string text, ICommandOutput output)
    {
        try
        {
            db.Initialize();
            var (high, low) = db.EncodeAndStore(text);

            output.WriteLine($"Content: \"{text}\"");
            output.WriteLine($"Root ID: {high}:{low}");

            return 0;
        }
        catch (Exception ex)
        {
            output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Decode content from ID.
    /// </summary>
    public static int DecodeContent(IDatabaseService db, string idStr, ICommandOutput output)
    {
        var parts = idStr.Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], out var high) ||
            !long.TryParse(parts[1], out var low))
        {
            output.WriteError("Error: ID must be in format 'high:low' (e.g., 123456789:987654321)");
            return 1;
        }

        try
        {
            db.Initialize();
            var text = db.Decode(high, low);

            output.WriteLine($"Root ID: {high}:{low}");
            output.WriteLine($"Content: \"{text}\"");
            output.WriteLine($"Length:  {text.Length} characters");

            return 0;
        }
        catch (Exception ex)
        {
            output.WriteError($"Error: {ex.Message}");
            return 1;
        }
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
