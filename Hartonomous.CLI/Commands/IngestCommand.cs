using Hartonomous.Commands;
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

        return IngestCommandHandler.Execute(
            DatabaseService.Instance,
            path,
            sparsity,
            quiet,
            ConsoleCommandOutput.Instance);
    }
}
