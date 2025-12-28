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

        if (string.IsNullOrWhiteSpace(question))
        {
            Console.Error.WriteLine("Error: Question required.");
            return 1;
        }

        try
        {
            var db = DatabaseService.Instance;
            db.Initialize();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Q: ");
            Console.ResetColor();
            Console.WriteLine(question);

            var (answer, confidence, path) = db.Ask(question, maxHops);

            if (string.IsNullOrEmpty(answer))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nNo answer found in the knowledge graph.");
                Console.WriteLine("Try ingesting more content or a model first.");
                Console.ResetColor();
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\nA: ");
            Console.ResetColor();
            Console.WriteLine(answer);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   Confidence: {confidence:P1}");
            Console.ResetColor();

            if (verbose && path.Length > 0)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Inference path:");
                for (int i = 0; i < path.Length; i++)
                {
                    var hop = path[i];
                    Console.WriteLine($"  [{i + 1}] {hop.FromText} → {hop.ToText} (w={hop.Weight:F3})");
                }
                Console.ResetColor();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
