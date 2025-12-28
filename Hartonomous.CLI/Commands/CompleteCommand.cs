using Hartonomous.Commands;
using Hartonomous.Core.Services;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Complete a prompt using the semantic substrate.
/// Replaces LLM text completion with graph traversal.
/// </summary>
public sealed class CompleteCommand : ICommand
{
    public string Name => "complete";
    public string Description => "Complete a prompt using semantic graph traversal.";

    public string Usage => """
        Usage: hartonomous complete <prompt> [options]

        Options:
          --max-tokens, -n <count>    Maximum tokens to generate (default: 20)
          --temperature, -t <value>   Sampling temperature 0-2 (default: 0.7)
          --seed, -s <value>          Random seed for reproducibility

        Examples:
          hartonomous complete "The king sat on his"
          hartonomous complete "Once upon a time" -n 50 -t 1.0
          hartonomous complete "Machine learning is" --max-tokens 30
        """;

    public int Execute(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Prompt required.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        // Parse arguments
        string prompt = "";
        int maxTokens = 20;
        double temperature = 0.7;
        ulong seed = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "--max-tokens" or "-n" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var n))
                    maxTokens = Math.Clamp(n, 1, 1000);
            }
            else if (arg is "--temperature" or "-t" && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], out var t))
                    temperature = Math.Clamp(t, 0.0, 2.0);
            }
            else if (arg is "--seed" or "-s" && i + 1 < args.Length)
            {
                if (ulong.TryParse(args[++i], out var s))
                    seed = s;
            }
            else if (!arg.StartsWith('-'))
            {
                prompt = arg;
            }
        }

        return CompleteCommandHandler.Execute(
            DatabaseService.Instance,
            prompt,
            maxTokens,
            temperature,
            seed,
            ConsoleCommandOutput.Instance);
    }
}
