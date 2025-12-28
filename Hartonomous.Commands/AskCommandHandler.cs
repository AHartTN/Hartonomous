using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Commands;

/// <summary>
/// Shared logic for the 'ask' command.
/// </summary>
public static class AskCommandHandler
{
    /// <summary>
    /// Execute the ask command.
    /// </summary>
    public static int Execute(
        IDatabaseService db,
        string question,
        int maxHops,
        bool verbose,
        ICommandOutput output)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            output.WriteError("Error: Question required.");
            return 1;
        }

        try
        {
            db.Initialize();

            output.Write("Q: ", ConsoleColor.Cyan);
            output.WriteLine(question);

            var (answer, confidence, path) = db.Ask(question, maxHops);

            if (string.IsNullOrEmpty(answer))
            {
                output.WriteLine();
                output.WriteLine("No answer found in the knowledge graph.", ConsoleColor.Yellow);
                output.WriteLine("Try ingesting more content or a model first.", ConsoleColor.Yellow);
                return 1;
            }

            output.WriteLine();
            output.Write("A: ", ConsoleColor.Green);
            output.WriteLine(answer);

            output.WriteLine($"   Confidence: {confidence:P1}", ConsoleColor.DarkGray);

            if (verbose && path.Length > 0)
            {
                output.WriteLine();
                output.WriteLine("Inference path:", ConsoleColor.DarkGray);
                for (int i = 0; i < path.Length; i++)
                {
                    var hop = path[i];
                    output.WriteLine($"  [{i + 1}] {hop.FromText} → {hop.ToText} (w={hop.Weight:F3})", ConsoleColor.DarkGray);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }
}
