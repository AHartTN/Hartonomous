using Hartonomous.Commands;
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
        var output = new TextWriterCommandOutput(context.Output, context.Error);

        AskCommandHandler.Execute(
            DatabaseService.Instance,
            question,
            maxHops: 10,
            verbose: true,
            output);
    }
}
