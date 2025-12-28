using Hartonomous.Core.Services;

namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Ask a single question and get an answer with inference path.
/// </summary>
public sealed class AskCommand : IReplCommand
{
    public string Name => "ask";
    public IReadOnlyList<string> Aliases => ["a", "?"]; 
    public string Description => "Ask a question and see the inference path.";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        if (args.Length == 0)
        {
            context.Error.WriteLine("Usage: ask <question>");
            context.Error.WriteLine("Example: ask What is the capital of France?");
            return;
        }

        var question = string.Join(" ", args.ToArray());
        var db = DatabaseService.Instance;

        try
        {
            context.Output.WriteLine($"Question: {question}");
            context.Output.WriteLine();

            var (answer, confidence, path) = db.Ask(question, maxHops: 10);

            if (string.IsNullOrEmpty(answer))
            {
                context.Output.WriteLine("No answer found.");
                return;
            }

            context.Output.WriteLine($"Answer: {answer}");
            context.Output.WriteLine($"Confidence: {confidence:P1}");
            context.Output.WriteLine();

            if (path.Length > 0)
            {
                context.Output.WriteLine("Inference Path:");
                for (int i = 0; i < path.Length; i++)
                {
                    var hop = path[i];
                    context.Output.WriteLine($"  [{i + 1}] {hop.FromText} -> {hop.ToText} (Weight: {hop.Weight:F4})");
                }
            }
        }
        catch (Exception ex)
        {
            context.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}
