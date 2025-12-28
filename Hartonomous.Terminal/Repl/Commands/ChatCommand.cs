using Hartonomous.Commands;
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
        var db = DatabaseService.Instance;
        var output = new ReplCommandOutput(context);

        ChatCommandHandler.RunChat(
            db,
            output,
            Console.ReadLine);
    }
}
