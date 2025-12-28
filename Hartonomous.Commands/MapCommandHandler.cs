using System.Globalization;
using System.Text;
using Hartonomous.Core.Entities;

namespace Hartonomous.Commands;

/// <summary>
/// Shared logic for mapping codepoints to tesseract coordinates.
/// Used by both CLI and Terminal applications.
/// </summary>
public static class MapCommandHandler
{
    /// <summary>
    /// Map a single codepoint to its tesseract coordinates.
    /// </summary>
    public static int MapSingle(int codepoint, ICommandOutput output)
    {
        try
        {
            var atom = Atom.Create(codepoint);
            PrintAtom(atom, output);
            return 0;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Map a range of codepoints to tesseract coordinates.
    /// </summary>
    public static int MapRange(int start, int end, ICommandOutput output, int maxRange = 10000)
    {
        if (start > end)
        {
            output.WriteError($"Error: Invalid range {start}-{end}. Start must be <= end.");
            return 1;
        }

        if (end - start > maxRange)
        {
            output.WriteError($"Error: Range too large ({end - start + 1} codepoints). Maximum is {maxRange}.");
            return 1;
        }

        var atoms = Atom.CreateRange(start, end);
        
        // Batch output to reduce I/O overhead
        var sb = new StringBuilder(atoms.Count * 200);
        foreach (var atom in atoms)
        {
            FormatAtom(atom, sb);
        }
        output.Output.Write(sb);

        output.WriteLine($"\nTotal: {atoms.Count} codepoints mapped.");
        return 0;
    }

    /// <summary>
    /// Print a single atom's details.
    /// </summary>
    public static void PrintAtom(Atom atom, ICommandOutput output)
    {
        output.WriteLine($"Codepoint: U+{atom.Codepoint:X4} ({atom.Codepoint})");
        output.WriteLine($"Character: '{atom.Character ?? "N/A"}'");
        output.WriteLine($"Surface:   ({atom.SurfacePoint.X}, {atom.SurfacePoint.Y}, {atom.SurfacePoint.Z}, {atom.SurfacePoint.W})");
        output.WriteLine($"Face:      {atom.SurfacePoint.Face}");
        output.WriteLine($"Hilbert:   {atom.HilbertIndex}");
    }

    private static void FormatAtom(Atom atom, StringBuilder sb)
    {
        sb.Append("U+").Append(atom.Codepoint.ToString("X4", CultureInfo.InvariantCulture)).Append(" '").Append(atom.Character ?? "N/A").AppendLine("':");
        sb.Append("  Surface: (").Append(atom.SurfacePoint.X).Append(", ").Append(atom.SurfacePoint.Y).Append(", ")
          .Append(atom.SurfacePoint.Z).Append(", ").Append(atom.SurfacePoint.W).Append(") Face=").AppendLine(atom.SurfacePoint.Face.ToString());
        sb.Append("  Hilbert: ").AppendLine(atom.HilbertIndex.ToString());
    }
}
