using Hartonomous.Core.Services;

namespace Hartonomous.CLI.Commands;

/// <summary>
/// Command to validate Unicode codepoints.
/// </summary>
public sealed class ValidateCommand : ICommand
{
    public string Name => "validate";

    public string Description => "Validate Unicode codepoints.";

    public string Usage => """
        Usage: hartonomous validate <codepoint|range>

        Checks if codepoints are valid Unicode scalar values.

        Examples:
          hartonomous validate U+0041
          hartonomous validate 0xD800
          hartonomous validate 0-1000
        """;

    public int Execute(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Missing codepoint argument.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var input = args[0];

        if (TryParseRange(input, out var start, out var end))
        {
            return ValidateRange(start, end);
        }

        if (TryParseCodepoint(input, out var codepoint))
        {
            return ValidateSingle(codepoint);
        }

        Console.Error.WriteLine($"Error: Invalid input '{input}'.");
        return 1;
    }

    private static int ValidateSingle(int codepoint)
    {
        var isValid = UnicodeValidationService.IsValidScalarValue(codepoint);
        var status = isValid ? "VALID" : "INVALID";
        var reason = GetInvalidReason(codepoint, isValid);

        Console.WriteLine($"U+{codepoint:X4}: {status}{reason}");
        return isValid ? 0 : 1;
    }

    private static int ValidateRange(int start, int end)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var validCount = 0;
        var invalidCount = 0;
        var invalidSamples = new System.Collections.Concurrent.ConcurrentBag<(int Codepoint, string Reason)>();
        const int MaxSamples = 10;

        // Parallel validation with thread-safe counting
        Parallel.For(start, end + 1, () => (valid: 0, invalid: 0), (cp, _, localCounts) =>
        {
            var isValid = UnicodeValidationService.IsValidScalarValue(cp);
            if (isValid)
            {
                return (localCounts.valid + 1, localCounts.invalid);
            }
            else
            {
                // Only collect samples if we haven't hit the limit
                if (invalidSamples.Count < MaxSamples)
                {
                    invalidSamples.Add((cp, GetInvalidReason(cp, false)));
                }
                return (localCounts.valid, localCounts.invalid + 1);
            }
        }, localCounts =>
        {
            Interlocked.Add(ref validCount, localCounts.valid);
            Interlocked.Add(ref invalidCount, localCounts.invalid);
        });

        // Output collected invalid samples (sorted for deterministic output)
        foreach (var (cp, reason) in invalidSamples.OrderBy(x => x.Codepoint).Take(MaxSamples))
        {
            Console.WriteLine($"U+{cp:X4}: INVALID{reason}");
        }

        Console.WriteLine();
        Console.WriteLine($"Range U+{start:X4}-U+{end:X4}:");
        Console.WriteLine($"  Valid:   {validCount:N0}");
        Console.WriteLine($"  Invalid: {invalidCount:N0}");

        if (invalidCount > MaxSamples)
        {
            Console.WriteLine($"  (showing first {MaxSamples} invalid codepoints)");
        }

        return invalidCount == 0 ? 0 : 1;
    }

    private static string GetInvalidReason(int codepoint, bool isValid)
    {
        if (isValid)
            return string.Empty;

        if (codepoint < 0)
            return " (negative value)";

        if (codepoint >= 0xD800 && codepoint <= 0xDFFF)
            return " (surrogate)";

        if (codepoint > 0x10FFFF)
            return " (exceeds Unicode maximum)";

        return " (invalid scalar)";
    }

    private static bool TryParseRange(string input, out int start, out int end)
    {
        start = end = 0;

        var dashIndex = input.IndexOf('-', 1);
        if (dashIndex < 0)
            return false;

        var startPart = input[..dashIndex];
        var endPart = input[(dashIndex + 1)..];

        return TryParseCodepoint(startPart, out start) && TryParseCodepoint(endPart, out end);
    }

    private static bool TryParseCodepoint(string input, out int codepoint)
    {
        codepoint = 0;

        if (input.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out codepoint);
        }

        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out codepoint);
        }

        return int.TryParse(input, out codepoint);
    }
}
