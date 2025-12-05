using System.Security.Cryptography;
using System.Text;
using Hartonomous.Core.Domain.Common;

namespace Hartonomous.Core.Domain.Math;

/// <summary>
/// Projects high-dimensional content hashes (256-bit) into scalar spatial coordinates
/// using a deterministically orthonormalized basis (Gram-Schmidt).
/// 
/// This replaces the "lazy" bit-slicing approach with a mathematically rigorous
/// projection that preserves high-dimensional locality in the lower-dimensional mapping.
/// </summary>
public static class GramSchmidtProjector
{
    private const int VectorSize = 32; // 256 bits = 32 bytes
    private static readonly double[] BasisVector;

    static GramSchmidtProjector()
    {
        BasisVector = InitializeOrthonormalBasis();
    }

    /// <summary>
    /// Projects a 256-bit hash onto a scalar spatial dimension [0, 2^Precision-1].
    /// Incorporates modality as a subspace bias if provided.
    /// </summary>
    public static uint Project(Hash256 hash, string? modality = null, int precision = 21)
    {
        // 1. Convert hash to vector
        double[] contentVector = BytesToVector(hash.Bytes);

        // 2. Apply Modality Bias (if present)
        // "Modality is one of the values we use to determine the landmark projection"
        if (!string.IsNullOrEmpty(modality))
        {
            double[] modalityVector = GetModalityVector(modality);
            // Combine vectors (simple addition in high-dimensional space)
            for (int i = 0; i < VectorSize; i++)
            {
                contentVector[i] += modalityVector[i];
            }
        }

        // 3. Project onto the Orthonormal Basis (Dot Product)
        // Since we are projecting to 1 dimension (X), we dot with the single basis vector.
        double projection = DotProduct(contentVector, BasisVector);

        // 4. Map projection to integer range [0, 2^Precision - 1]
        // Projection is roughly normally distributed around 0. 
        // We use a sigmoid-like transform to map R -> [0, 1].
        double normalized = Sigmoid(projection);
        
        ulong maxVal = (1UL << precision) - 1;
        return (uint)(normalized * maxVal);
    }

    private static double[] InitializeOrthonormalBasis()
    {
        // Deterministic seed for reproducibility
        // We want a "random" vector that covers the space well
        var seed = Encoding.UTF8.GetBytes("Hartonomous-Universal-Geometry-Basis-Seed-v1");
        using var rng = SHA256.Create();
        var hash = rng.ComputeHash(seed);
        
        var vector = BytesToVector(hash);

        // Normalize (Gram-Schmidt on a single vector is just normalization)
        return Normalize(vector);
    }

    private static double[] GetModalityVector(string modality)
    {
        // Deterministically generate a vector for this modality
        var seed = Encoding.UTF8.GetBytes($"Modality-Basis-{modality.ToUpperInvariant()}");
        using var rng = SHA256.Create();
        var hash = rng.ComputeHash(seed);
        
        // Normalize to ensure it doesn't overwhelm the content signal
        return Normalize(BytesToVector(hash));
    }

    private static double[] BytesToVector(byte[] bytes)
    {
        var vector = new double[VectorSize];
        for (int i = 0; i < VectorSize; i++)
        {
            // Treat each byte as a dimension value [-128, 127] for centering
            vector[i] = bytes[i] - 128.0;
        }
        return vector;
    }

    private static double DotProduct(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < VectorSize; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    private static double[] Normalize(double[] v)
    {
        double norm = System.Math.Sqrt(DotProduct(v, v));
        if (norm == 0) return v; // Should not happen with random hashes

        var result = new double[VectorSize];
        for (int i = 0; i < VectorSize; i++)
        {
            result[i] = v[i] / norm;
        }
        return result;
    }

    private static double Sigmoid(double x)
    {
        // Logistic sigmoid to map (-inf, inf) -> (0, 1)
        // Scale x to make better use of the range. 
        // Dot product of two normalized 32-dim vectors of magnitude ~1 can be range [-1, 1]??
        // No, input vectors are magnitude ~sqrt(32*128^2) approx 700.
        // Basis is magnitude 1.
        // So projection is magnitude ~700.
        // Sigmoid needs inputs in approx [-6, 6].
        // We scale down by a factor.
        return 1.0 / (1.0 + System.Math.Exp(-x / 100.0));
    }
}
