using System.Security.Cryptography;
using System.Text;

namespace Hartonomous.CodeAtomizer.Core.Spatial;

/// <summary>
/// Landmark-based spatial positioning system.
/// Maps semantic concepts to 3D coordinates using fixed landmarks.
/// </summary>
public static class LandmarkProjection
{
    // ============================================================================
    // Landmark Coordinates (Fixed Points in Semantic Space)
    // ============================================================================

    /// <summary>
    /// Modality landmarks (X-axis)
    /// Fundamental types of information
    /// </summary>
    private static readonly Dictionary<string, double> ModalityLandmarks = new()
    {
        ["code"] = 0.1,
        ["text"] = 0.3,
        ["numeric"] = 0.4,
        ["image"] = 0.5,
        ["audio"] = 0.7,
        ["video"] = 0.9,
        ["binary"] = 0.95
    };

    /// <summary>
    /// Category landmarks (Y-axis)
    /// Semantic roles and purposes
    /// </summary>
    private static readonly Dictionary<string, double> CategoryLandmarks = new()
    {
        // Structural elements
        ["file"] = 0.05,
        ["namespace"] = 0.1,
        ["class"] = 0.15,
        ["interface"] = 0.18,
        ["struct"] = 0.2,
        
        // Behavioral elements
        ["method"] = 0.3,
        ["function"] = 0.32,
        ["property"] = 0.35,
        ["event"] = 0.38,
        
        // Data elements
        ["field"] = 0.5,
        ["variable"] = 0.52,
        ["parameter"] = 0.55,
        ["literal"] = 0.58,
        
        // Metadata elements
        ["comment"] = 0.7,
        ["attribute"] = 0.72,
        ["annotation"] = 0.75,
        
        // Compositional elements
        ["statement"] = 0.85,
        ["expression"] = 0.87,
        ["operator"] = 0.9,
        ["keyword"] = 0.95
    };

    /// <summary>
    /// Specificity landmarks (Z-axis)
    /// Level of abstraction
    /// </summary>
    private static readonly Dictionary<string, double> SpecificityLandmarks = new()
    {
        ["abstract"] = 0.1,      // Pure concepts, interfaces
        ["generic"] = 0.3,       // Type parameters, templates
        ["concrete"] = 0.5,      // Implementations, classes
        ["instance"] = 0.7,      // Objects, variables
        ["literal"] = 0.9        // Constants, raw values
    };

    /// <summary>
    /// Compute 3D spatial position for an atom using landmark projection.
    /// </summary>
    /// <param name="modality">Modality type (code, text, image, etc.)</param>
    /// <param name="category">Semantic category (class, method, field, etc.)</param>
    /// <param name="specificity">Abstraction level (abstract, concrete, literal)</param>
    /// <param name="identifier">Unique identifier for fine-tuning position</param>
    /// <returns>3D spatial position</returns>
    public static (double X, double Y, double Z) ComputePosition(
        string modality,
        string category,
        string? specificity = null,
        string? identifier = null)
    {
        // Base coordinates from landmarks
        var x = ModalityLandmarks.GetValueOrDefault(modality.ToLowerInvariant(), 0.5);
        var y = CategoryLandmarks.GetValueOrDefault(category.ToLowerInvariant(), 0.5);
        var z = specificity != null
            ? SpecificityLandmarks.GetValueOrDefault(specificity.ToLowerInvariant(), 0.5)
            : 0.5;

        // Fine-tune position using identifier hash (prevents exact overlaps)
        if (!string.IsNullOrEmpty(identifier))
        {
            var hash = ComputeHash(identifier);
            
            // Add small perturbation (▒0.05) based on hash
            x += (hash[0] % 100 - 50) / 1000.0;
            y += (hash[1] % 100 - 50) / 1000.0;
            z += (hash[2] % 100 - 50) / 1000.0;

            // Clamp to [0, 1]
            x = Math.Clamp(x, 0.0, 1.0);
            y = Math.Clamp(y, 0.0, 1.0);
            z = Math.Clamp(z, 0.0, 1.0);
        }

        return (x, y, z);
    }

    /// <summary>
    /// Infer specificity level from node type and context.
    /// </summary>
    public static string InferSpecificity(string nodeType, bool isAbstract = false, bool hasValue = false)
    {
        if (isAbstract)
            return "abstract";

        return nodeType.ToLowerInvariant() switch
        {
            "interface" => "abstract",
            "abstract-class" => "abstract",
            "generic-parameter" => "generic",
            "class" => "concrete",
            "method" => "concrete",
            "field" => "concrete",
            "variable" => "instance",
            "parameter" => "instance",
            "literal" => "literal",
            "constant" => "literal",
            _ when hasValue => "literal",
            _ => "concrete"
        };
    }

    /// <summary>
    /// Compute distance between two spatial positions (Euclidean).
    /// </summary>
    public static double ComputeDistance(
        (double X, double Y, double Z) pos1,
        (double X, double Y, double Z) pos2)
    {
        var dx = pos1.X - pos2.X;
        var dy = pos1.Y - pos2.Y;
        var dz = pos1.Z - pos2.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Get nearest landmark category for a given Y coordinate.
    /// </summary>
    public static string GetNearestCategory(double y)
    {
        return CategoryLandmarks
            .OrderBy(kv => Math.Abs(kv.Value - y))
            .First()
            .Key;
    }

    /// <summary>
    /// Get all landmarks for debugging/visualization.
    /// </summary>
    public static Dictionary<string, (double X, double Y, double Z)> GetAllLandmarks()
    {
        var landmarks = new Dictionary<string, (double X, double Y, double Z)>();

        foreach (var modality in ModalityLandmarks)
        {
            foreach (var category in CategoryLandmarks)
            {
                foreach (var specificity in SpecificityLandmarks)
                {
                    var key = $"{modality.Key}:{category.Key}:{specificity.Key}";
                    landmarks[key] = (modality.Value, category.Value, specificity.Value);
                }
            }
        }

        return landmarks;
    }

    private static byte[] ComputeHash(string text)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Compute 3D spatial position AND Hilbert index for an atom using landmark projection.
    /// </summary>
    /// <param name="modality">Modality type (code, text, image, etc.)</param>
    /// <param name="category">Semantic category (class, method, field, etc.)</param>
    /// <param name="specificity">Abstraction level (abstract, concrete, literal)</param>
    /// <param name="identifier">Unique identifier for fine-tuning position</param>
    /// <param name="hilbertOrder">Hilbert curve resolution (default 10 = 1024│)</param>
    /// <returns>3D position and Hilbert index</returns>
    public static (double X, double Y, double Z, long HilbertIndex) ComputePositionWithHilbert(
        string modality,
        string category,
        string? specificity = null,
        string? identifier = null,
        int hilbertOrder = 10)
    {
        var (x, y, z) = ComputePosition(modality, category, specificity, identifier);
        
        // Compute Hilbert index for this 3D position
        var hilbertIndex = HilbertCurve.Encode(x, y, z, hilbertOrder);
        
        return (x, y, z, hilbertIndex);
    }
}
