using Hartonomous.Core.Services;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Command to display system information and statistics.
/// </summary>
public sealed class InfoCommand : ICommand
{
    public string Name => "info";

    public string Description => "Display system information and Unicode statistics.";

    public string Usage => "Usage: hartonomous info";

    public int Execute(ReadOnlySpan<string> args)
    {
        var systemInfo = SystemInformationService.GetSystemInformation();

        Console.WriteLine("=== Hartonomous System Information ===\n");

        Console.WriteLine("Unicode Coverage:");
        Console.WriteLine($"  Total Valid Codepoints: {systemInfo.TotalCodepointCount:N0}");
        Console.WriteLine($"  Maximum Codepoint:      U+{systemInfo.MaximumCodepoint:X}");
        Console.WriteLine();

        Console.WriteLine("Native Library:");
        Console.WriteLine($"  Version: {systemInfo.NativeLibraryVersion}");
        Console.WriteLine();

        Console.WriteLine("Coordinate System:");
        Console.WriteLine("  Dimension: 4D (Tesseract/Hypercube)");
        Console.WriteLine("  Faces:     8 (XMin, XMax, YMin, YMax, ZMin, ZMax, WMin, WMax)");
        Console.WriteLine("  Index:     128-bit Hilbert Curve");
        Console.WriteLine();

        return 0;
    }
}
