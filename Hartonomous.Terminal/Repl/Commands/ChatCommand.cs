using Hartonomous.Core.Services;

namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Enter interactive chat mode for multi-turn conversation.
/// </summary>
public sealed class ChatCommand : IReplCommand
{
    public string Name => "chat";
    public IReadOnlyList<string> Aliases => ["c"];
    public string Description => "Enter interactive chat mode.";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        context.Output.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        context.Output.WriteLine("║                    Interactive Chat Mode                      ║");
        context.Output.WriteLine("║  Type your questions naturally. Type 'exit' or Ctrl+C to quit ║");
        context.Output.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        context.Output.WriteLine();

        var db = DatabaseService.Instance;
        var history = new List<(string Question, string Answer)>();

        while (true)
        {
            context.Output.Write("You: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                context.Output.WriteLine();
                context.Output.WriteLine("Exiting chat mode...");
                return;
            }

            try
            {
                var (answer, confidence, path) = db.Ask(input, maxHops: 10);

                context.Output.WriteLine();
                context.Output.Write("Hartonomous: ");

                if (string.IsNullOrEmpty(answer))
                {
                    context.Output.WriteLine("I don't have enough information to answer that question.");
                }
                else
                {
                    context.Output.WriteLine(answer);
                    context.Output.WriteLine($"  [Confidence: {confidence:P1}, Path: {path.Length} hops]");
                }

                history.Add((input, answer ?? "(no answer)"));
                context.Output.WriteLine();
            }
            catch (Exception ex)
            {
                context.Error.WriteLine($"Error: {ex.Message}");
                context.Output.WriteLine();
            }
        }
    }
}
