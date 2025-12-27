using Hartonomous.Core.Native;

namespace Hartonomous.Core.Services;

/// <summary>
/// Provides validation services for Unicode codepoints.
/// Validates that codepoint values conform to the Unicode Standard.
/// </summary>
/// <remarks>
/// Thread-safe. All methods delegate to the native library for validation.
/// A valid Unicode scalar value is any codepoint except high-surrogate and low-surrogate code points
/// (U+D800 to U+DFFF inclusive).
/// </remarks>
public static class UnicodeValidationService
{
    /// <summary>
    /// Determines whether the specified codepoint is a valid Unicode scalar value.
    /// </summary>
    /// <param name="codepoint">The Unicode codepoint to validate.</param>
    /// <returns>
    /// <c>true</c> if the codepoint is a valid Unicode scalar value; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This method validates against the full Unicode range (U+0000 to U+10FFFF),
    /// excluding surrogate pair values (U+D800 to U+DFFF).
    /// </remarks>
    public static bool IsValidScalarValue(int codepoint)
    {
        try
        {
            return NativeInterop.IsValidScalar(codepoint) != 0;
        }
        catch
        {
            return false;
        }
    }
}
