namespace Hartonomous.Core.Services;

/// <summary>
/// Represents runtime system information about the Hartonomous native library.
/// This immutable record contains diagnostic and versioning metadata.
/// </summary>
/// <param name="TotalCodepointCount">The total number of valid Unicode codepoints supported by the system.</param>
/// <param name="MaximumCodepoint">The highest valid Unicode codepoint value (typically U+10FFFF).</param>
/// <param name="NativeLibraryVersion">The semantic version string of the native Hartonomous library.</param>
public readonly record struct SystemInformation(
    int TotalCodepointCount,
    int MaximumCodepoint,
    string NativeLibraryVersion);
