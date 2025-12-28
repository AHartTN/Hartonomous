using Hartonomous.Commands;
using Hartonomous.Core.Services;

namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Complete a text prompt using graph traversal generation.
/// </summary>
public sealed class CompleteReplCommand : IReplCommand
{
    public string Name => "complete";
    public IReadOnlyList<string> Aliases => ["gen", "g"];
    public string Description => "Complete a text prompt (generate continuation).";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        if (args.Length == 0)
        {
            context.Error.WriteLine("Usage: complete <prompt> [--tokens N] [--temp T]");
            context.Error.WriteLine("Example: complete The quick brown fox --tokens 50 --temp 0.7");
            return;
        }

        // Parse args: extract prompt and options
        var argList = args.ToArray().ToList();
        var maxTokens = 50;
        var temperature = 0.7;

        // Extract --tokens
        var tokensIdx = argList.FindIndex(a => a.Equals("--tokens", StringComparison.OrdinalIgnoreCase));
        if (tokensIdx >= 0 && tokensIdx + 1 < argList.Count)
        {
            if (int.TryParse(argList[tokensIdx + 1], out var t))
                maxTokens = t;
            argList.RemoveRange(tokensIdx, 2);
        }

        // Extract --temp
        var tempIdx = argList.FindIndex(a => a.Equals("--temp", StringComparison.OrdinalIgnoreCase));
        if (tempIdx >= 0 && tempIdx + 1 < argList.Count)
        {
            if (double.TryParse(argList[tempIdx + 1], out var tp))
                temperature = tp;
            argList.RemoveRange(tempIdx, 2);
        }

        var prompt = string.Join(" ", argList);
        
        if (string.IsNullOrWhiteSpace(prompt))
        {
            context.Error.WriteLine("Error: No prompt provided.");
            return;
        }

        var output = new TextWriterCommandOutput(context.Output, context.Error);

        CompleteCommandHandler.Execute(
            DatabaseService.Instance,
            prompt,
            maxTokens,
            temperature,
            seed: 0,
            output);
    }
}
