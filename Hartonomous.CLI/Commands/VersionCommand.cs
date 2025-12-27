using Hartonomous.Core.Services;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Command to display version information.
/// </summary>
public sealed class VersionCommand : ICommand
{
    public string Name => "version";

    public string Description => "Display version information.";

    public string Usage => "Usage: hartonomous version";

    public int Execute(ReadOnlySpan<string> args)
    {
        var systemInfo = SystemInformationService.GetSystemInformation();

        Console.WriteLine($"Hartonomous CLI v{GetVersion()}");
        Console.WriteLine($"Native Library: {systemInfo.NativeLibraryVersion}");
        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        return 0;
    }

    private static string GetVersion()
    {
        var assembly = typeof(VersionCommand).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }
}
