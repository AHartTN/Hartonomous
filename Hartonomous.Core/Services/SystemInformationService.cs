using Hartonomous.Core.Native;

namespace Hartonomous.Core.Services;

/// <summary>
/// Provides runtime system information about the Hartonomous native library and platform capabilities.
/// This service exposes diagnostic and versioning information for monitoring and debugging purposes.
/// </summary>
/// <remarks>
/// Thread-safe. All methods are idempotent and can be called concurrently.
/// </remarks>
public static class SystemInformationService
{
    /// <summary>
    /// Retrieves comprehensive system information including native library version,
    /// supported codepoint ranges, and platform capabilities.
    /// </summary>
    /// <returns>A <see cref="SystemInformation"/> record containing all system metadata.</returns>
    public static SystemInformation GetSystemInformation()
    {
        try
        {
            return new SystemInformation(
                TotalCodepointCount: NativeInterop.GetCodepointCount(),
                MaximumCodepoint: NativeInterop.GetMaxCodepoint(),
                NativeLibraryVersion: NativeInterop.GetVersion());
        }
        catch
        {
            return new SystemInformation(
                TotalCodepointCount: 0,
                MaximumCodepoint: 0,
                NativeLibraryVersion: "unavailable");
        }
    }
}
