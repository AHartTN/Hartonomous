namespace Hartonomous.Commands;

/// <summary>
/// Output abstraction for command execution.
/// Allows commands to work with different output targets (console, REPL, etc.)
/// </summary>
public interface ICommandOutput
{
    /// <summary>
    /// Standard output writer.
    /// </summary>
    TextWriter Output { get; }
    
    /// <summary>
    /// Error output writer.
    /// </summary>
    TextWriter ErrorWriter { get; }
    
    /// <summary>
    /// Write text with optional foreground color (if supported).
    /// </summary>
    void Write(string text, ConsoleColor? color = null);
    
    /// <summary>
    /// Write a line with optional foreground color (if supported).
    /// </summary>
    void WriteLine(string text = "", ConsoleColor? color = null);
    
    /// <summary>
    /// Write an error message.
    /// </summary>
    void WriteError(string text);
}

/// <summary>
/// Console-based command output with color support.
/// </summary>
public sealed class ConsoleCommandOutput : ICommandOutput
{
    public static ConsoleCommandOutput Instance { get; } = new();
    
    public TextWriter Output => Console.Out;
    public TextWriter ErrorWriter => Console.Error;
    
    public void Write(string text, ConsoleColor? color = null)
    {
        if (color.HasValue)
        {
            Console.ForegroundColor = color.Value;
            Console.Write(text);
            Console.ResetColor();
        }
        else
        {
            Console.Write(text);
        }
    }
    
    public void WriteLine(string text = "", ConsoleColor? color = null)
    {
        if (color.HasValue)
        {
            Console.ForegroundColor = color.Value;
            Console.WriteLine(text);
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine(text);
        }
    }
    
    public void WriteError(string text)
    {
        Console.Error.WriteLine(text);
    }
}

/// <summary>
/// TextWriter-based command output (no color support).
/// </summary>
public sealed class TextWriterCommandOutput : ICommandOutput
{
    public TextWriterCommandOutput(TextWriter output, TextWriter error)
    {
        Output = output;
        ErrorWriter = error;
    }
    
    public TextWriter Output { get; }
    public TextWriter ErrorWriter { get; }
    
    public void Write(string text, ConsoleColor? color = null)
    {
        Output.Write(text);
    }
    
    public void WriteLine(string text = "", ConsoleColor? color = null)
    {
        Output.WriteLine(text);
    }
    
    public void WriteError(string text)
    {
        ErrorWriter.WriteLine(text);
    }
}
