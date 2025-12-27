namespace Hartonomous.Terminal.Repl.Commands;

/// <summary>
/// Utility for parsing codepoints from various formats.
/// </summary>
internal static class CodepointParser
{
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

        // Surrogate pair (emoji, etc.)
        if (input.Length == 2 && char.IsSurrogatePair(input, 0))
        {
            codepoint = char.ConvertToUtf32(input, 0);
            return true;
        }

        // U+XXXX format
        if (input.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("u+", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out codepoint);
        }

        // 0xXXXX format
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out codepoint);
        }

        // Plain decimal
        return int.TryParse(input, out codepoint);
    }
}
