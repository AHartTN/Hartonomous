using System.Diagnostics;
using Hartonomous.Core.Services;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Ingest content into the Hartonomous substrate.
/// Supports text files, model packages, and directories.
/// </summary>
public sealed class IngestCommand : ICommand
{
    public string Name => "ingest";
    public string Description => "Ingest content (text, models, directories) into the substrate.";

    public string Usage => """
        Usage: hartonomous ingest <path> [options]

        Arguments:
          path          File or directory to ingest

        Options:
          --sparsity    Sparsity threshold for model weights (default: 1e-6)
          --quiet       Suppress progress output

        Supported content:
          - Text files (.txt, .md, .json, .yaml, .py, .cpp, etc.)
          - AI model packages (directories with tokenizer + .safetensors)
          - Any file (ingested as raw bytes)

        Examples:
          hartonomous ingest moby_dick.txt
          hartonomous ingest ./models/all-MiniLM-L6-v2/
          hartonomous ingest . --quiet
        """;

    public int Execute(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Path is required.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var path = args[0];
        var sparsity = 1e-6;
        var quiet = false;

        // Parse options
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sparsity" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], out sparsity))
                    {
                        Console.Error.WriteLine($"Error: Invalid sparsity value.");
                        return 1;
                    }
                    break;
                case "--quiet":
                    quiet = true;
                    break;
            }
        }

        // Resolve path
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            Console.Error.WriteLine($"Error: Path not found: {fullPath}");
            return 1;
        }

        try
        {
            var db = DatabaseService.Instance;

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
                Console.WriteLine($"Ingesting: {fullPath}");
                Console.WriteLine($"  Files: {fileCount:N0}");
                Console.WriteLine($"  Size:  {FormatBytes(totalBytes)}");
                Console.WriteLine();
                Console.WriteLine("Processing... (this may take a while for large files)");
                Console.Out.Flush();
            }

            var sw = Stopwatch.StartNew();
            var result = db.Ingest(fullPath, sparsity);
            sw.Stop();

            if (!quiet)
            {
                Console.WriteLine("============================================================");
                Console.WriteLine("INGESTION COMPLETE");
                Console.WriteLine("------------------------------------------------------------");
                Console.WriteLine($"{"Files:",-20} {result.FilesProcessed:N0}");
                Console.WriteLine($"{"Bytes:",-20} {result.BytesProcessed:N0}");
                Console.WriteLine($"{"Compositions:",-20} {result.CompositionsCreated:N0}");
                Console.WriteLine($"{"Relationships:",-20} {result.RelationshipsCreated:N0}");
                Console.WriteLine($"{"Errors:",-20} {result.Errors:N0}");
                Console.WriteLine($"{"Time:",-20} {result.Duration.TotalSeconds:F2}s");
                Console.WriteLine("============================================================");
            }

            return result.Errors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
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
