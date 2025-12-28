using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Commands;

/// <summary>
/// Shared logic for interactive chat mode.
/// </summary>
public static class ChatCommandHandler
{
    /// <summary>
    /// Run interactive chat session.
    /// </summary>
    public static void RunChat(
        IDatabaseService db, 
        ICommandOutput output,
        Func<string?> readLine,
        CancellationToken cancellationToken = default)
    {
        output.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        output.WriteLine("║                    Interactive Chat Mode                      ║");
        output.WriteLine("║  Type your questions naturally. Type 'exit' or Ctrl+C to quit ║");
        output.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        output.WriteLine();

        db.Initialize();

        while (!cancellationToken.IsCancellationRequested)
        {
            output.Write("You: ");
            var input = readLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine();
                output.WriteLine("Exiting chat mode...");
                return;
            }

            try
            {
                var (answer, confidence, path) = db.Ask(input, maxHops: 10);

                output.WriteLine();
                output.Write("Hartonomous: ");

                if (string.IsNullOrEmpty(answer))
                {
                    output.WriteLine("I don't have enough information to answer that question.");
                }
                else
                {
                    output.WriteLine(answer);
                    output.WriteLine($"  [Confidence: {confidence:P1}, Path: {path.Length} hops]");
                }

                output.WriteLine();
            }
            catch (Exception ex)
            {
                output.WriteError($"Error: {ex.Message}");
                output.WriteLine();
            }
        }
    }
}
