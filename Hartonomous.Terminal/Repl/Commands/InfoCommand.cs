using Hartonomous.Core.Services;

namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Display system information.
/// </summary>
public sealed class InfoCommand : IReplCommand
{
    public string Name => "info";
    public IReadOnlyList<string> Aliases => ["i"];
    public string Description => "Display system information.";

    public void Execute(ReadOnlySpan<string> args, ReplContext context)
    {
        var systemInfo = SystemInformationService.GetSystemInformation();

        context.Output.WriteLine("=== Hartonomous System Information ===");
        context.Output.WriteLine();
        context.Output.WriteLine($"Native Library:  {systemInfo.NativeLibraryVersion}");
        context.Output.WriteLine($"Codepoint Count: {systemInfo.TotalCodepointCount:N0}");
        context.Output.WriteLine($"Max Codepoint:   U+{systemInfo.MaximumCodepoint:X}");
    }
}
