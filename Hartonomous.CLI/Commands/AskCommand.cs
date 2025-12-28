using Hartonomous.Commands;
using Hartonomous.Core.Services;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Ask a question and get an answer from the knowledge graph.
/// Replaces LLM Q&A with graph traversal through semantic relationships.
/// </summary>
public sealed class AskCommand : ICommand
{
    public string Name => "ask";
    public string Description => "Ask a question using the semantic knowledge graph.";

    public string Usage => """
        Usage: hartonomous ask <question> [options]

        Options:
          --max-hops, -h <count>    Maximum inference hops (default: 6)
          --verbose, -v             Show inference path details

        The system traverses the knowledge graph to find paths from
        your question to potential answers. This replaces LLM inference
        with O(log n) spatial lookups and A* pathfinding.

        Examples:
          hartonomous ask "What is the capital of France?"
          hartonomous ask "Who wrote Moby Dick?" -v
          hartonomous ask "What is machine learning?" --max-hops 10
        """;

    public int Execute(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Question required.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        // Parse arguments
        string question = "";
        int maxHops = 6;
        bool verbose = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "--max-hops" or "-h" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var h))
                    maxHops = Math.Clamp(h, 1, 20);
            }
            else if (arg is "--verbose" or "-v")
            {
                verbose = true;
            }
            else if (!arg.StartsWith('-'))
            {
                question = arg;
            }
        }

        return AskCommandHandler.Execute(
            DatabaseService.Instance,
            question,
            maxHops,
            verbose,
            ConsoleCommandOutput.Instance);
    }
}
