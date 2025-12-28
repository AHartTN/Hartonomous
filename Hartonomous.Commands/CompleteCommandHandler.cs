using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Commands;

/// <summary>
/// Shared logic for the 'complete' command.
/// </summary>
public static class CompleteCommandHandler
{
    /// <summary>
    /// Execute the complete command.
    /// </summary>
    public static int Execute(
        IDatabaseService db,
        string prompt,
        int maxTokens,
        double temperature,
        ulong seed,
        ICommandOutput output)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            output.WriteError("Error: Prompt required.");
            return 1;
        }

        try
        {
            db.Initialize();

            output.Write(prompt, ConsoleColor.DarkGray);

            var generated = db.Complete(prompt, maxTokens, temperature, seed);

            output.WriteLine(generated, ConsoleColor.Green);

            return 0;
        }
        catch (Exception ex)
        {
            output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }
}
