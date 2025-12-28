using System.Globalization;

namespace Hartonomous.Commands;

/// <summary>
/// Shared codepoint parsing logic for CLI and Terminal commands.
/// </summary>
public static class CodepointParser
{
    /// <summary>
    /// Try to parse a string as a codepoint.
    /// Supports: single character, U+XXXX, 0xXXXX, decimal number.
    /// </summary>
    public static bool TryParse(string input, out int codepoint)
    {
        codepoint = 0;

        if (string.IsNullOrEmpty(input))
            return false;

        // Single character
        if (input.Length == 1)
        {
            codepoint = input[0];
            return true;
        }

        // Surrogate pair (2 chars representing a single codepoint)
        if (input.Length == 2 && char.IsSurrogatePair(input, 0))
        {
            codepoint = char.ConvertToUtf32(input, 0);
            return true;
        }

        // U+XXXX format
        if (input.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("u+", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codepoint);
        }

        // 0xXXXX format
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codepoint);
        }

        // Plain decimal
        return int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out codepoint);
    }

    /// <summary>
    /// Try to parse a range string (e.g., "U+0041-U+005A" or "0-127").
    /// </summary>
    public static bool TryParseRange(string input, out int start, out int end)
    {
        start = end = 0;

        var dashIndex = input.IndexOf('-', 1); // Skip first char to handle negative numbers
        if (dashIndex < 0)
            return false;

        var startPart = input[..dashIndex];
        var endPart = input[(dashIndex + 1)..];

        return TryParse(startPart, out start) && TryParse(endPart, out end);
    }
}
