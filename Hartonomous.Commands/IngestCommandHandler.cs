using System.Diagnostics;
using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Commands;

/// <summary>
/// Shared logic for the 'ingest' command.
/// </summary>
public static class IngestCommandHandler
{
    /// <summary>
    /// Execute the ingest command.
    /// </summary>
    public static int Execute(
        IDatabaseService db,
        string path,
        double sparsity,
        bool quiet,
        ICommandOutput output)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            output.WriteError($"Error: Path not found: {fullPath}");
            return 1;
        }

        try
        {
            // Show file info upfront
            long totalBytes = 0;
            int fileCount = 0;
            if (File.Exists(fullPath))
            {
                totalBytes = new FileInfo(fullPath).Length;
                fileCount = 1;
            }
            else if (Directory.Exists(fullPath))
            {
                var files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
                fileCount = files.Length;
                foreach (var f in files)
                {
                    try { totalBytes += new FileInfo(f).Length; } catch { }
                }
            }

            if (!quiet)
            {
                output.WriteLine($"Ingesting: {fullPath}");
                output.WriteLine($"  Files: {fileCount:N0}");
                output.WriteLine($"  Size:  {FormatBytes(totalBytes)}");
                output.WriteLine();
                output.WriteLine("Processing... (this may take a while for large files)");
                output.Output.Flush();
            }

            var sw = Stopwatch.StartNew();
            var result = db.Ingest(fullPath, sparsity);
            sw.Stop();

            if (!quiet)
            {
                output.WriteLine("============================================================");
                output.WriteLine("INGESTION COMPLETE");
                output.WriteLine("------------------------------------------------------------");
                output.WriteLine($"{"Files:",-20} {result.FilesProcessed:N0}");
                output.WriteLine($"{"Bytes:",-20} {result.BytesProcessed:N0}");
                output.WriteLine($"{"Compositions:",-20} {result.CompositionsCreated:N0}");
                output.WriteLine($"{"Relationships:",-20} {result.RelationshipsCreated:N0}");
                output.WriteLine($"{"Errors:",-20} {result.Errors:N0}");
                output.WriteLine($"{"Time:",-20} {result.Duration.TotalSeconds:F2}s");
                output.WriteLine("============================================================");
            }

            return result.Errors > 0 ? 1 : 0;
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
