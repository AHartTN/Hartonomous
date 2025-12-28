using Hartonomous.Commands;

namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Adapter that wraps ReplContext to implement ICommandOutput.
/// </summary>
public sealed class ReplCommandOutput : ICommandOutput
{
    private readonly ReplContext _context;

    public ReplCommandOutput(ReplContext context)
    {
        _context = context;
    }

    public TextWriter Output => _context.Output;
    public TextWriter ErrorWriter => _context.Error;

    public void Write(string text, ConsoleColor? color = null)
    {
        if (color.HasValue)
        {
            Console.ForegroundColor = color.Value;
            _context.Output.Write(text);
            Console.ResetColor();
        }
        else
        {
            _context.Output.Write(text);
        }
    }

    public void WriteLine(string text = "", ConsoleColor? color = null)
    {
        if (color.HasValue)
        {
            Console.ForegroundColor = color.Value;
            _context.Output.WriteLine(text);
            Console.ResetColor();
        }
        else
        {
            _context.Output.WriteLine(text);
        }
    }

    public void WriteError(string text) => _context.Error.WriteLine(text);
}
