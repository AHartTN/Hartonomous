using Hartonomous.Core.Application.Interfaces;
using System.IO.Compression;

namespace Hartonomous.Infrastructure.Services;

/// <summary>
/// Quantizes universal properties (entropy, compressibility, connectivity) to 21-bit integers
/// for storage in 4D Hilbert space (Y, Z, M dimensions)
/// </summary>
public class QuantizationService : IQuantizationService
{
    private const int MaxQuantizedValue = 2_097_151; // 2^21 - 1
    private const double Log2 = 0.693147180559945309417232121458; // ln(2)

    public (double yDimension, double zDimension, double mDimension) Quantize(byte[] data)
    {
        var entropy = CalculateShannonEntropy(data);
        var complexity = CalculateKolmogorovComplexity(data);
        var connectivity = CalculateGraphConnectivity(data);

        return (entropy, complexity, connectivity);
    }

    public double CalculateShannonEntropy(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 0;

        // Count byte frequencies
        var frequencies = new int[256];
        foreach (var b in data)
        {
            frequencies[b]++;
        }

        // Calculate Shannon entropy: H = -?(p(x) * log2(p(x)))
        double entropy = 0.0;
        var length = (double)data.Length;

        for (int i = 0; i < 256; i++)
        {
            if (frequencies[i] > 0)
            {
                var probability = frequencies[i] / length;
                entropy -= probability * (Math.Log(probability) / Log2);
            }
        }

        // Normalize to [0, 8] (max entropy for byte is 8 bits)
        // Then scale to [0, MaxQuantizedValue]
        var normalizedEntropy = Math.Clamp(entropy / 8.0, 0.0, 1.0);
        return normalizedEntropy * MaxQuantizedValue;
    }

    public double CalculateKolmogorovComplexity(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 0;

        try
        {
            // Approximate K(x) using gzip compression ratio
            // K(x) ? compressed_size / original_size
            using var memoryStream = new MemoryStream();
            using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
            {
                gzipStream.Write(data, 0, data.Length);
            }

            var compressedSize = memoryStream.Length;
            var originalSize = data.Length;
            
            // Compression ratio: 0 = highly compressible, 1 = incompressible
            var compressionRatio = Math.Clamp((double)compressedSize / originalSize, 0.0, 1.0);

            // Scale to [0, MaxQuantizedValue]
            // Lower compressibility (higher ratio) = higher Z value
            return compressionRatio * MaxQuantizedValue;
        }
        catch
        {
            // If compression fails, assume incompressible
            return MaxQuantizedValue;
        }
    }

    public double CalculateGraphConnectivity(byte[] data)
    {
        // Initial connectivity for new data is always 0.
        // Connectivity (M-dimension) is an emergent property of usage (reference count),
        // which is calculated dynamically via CalculateGraphConnectivity(long referenceCount).
        return 0.0;
    }

    /// <summary>
    /// Quantizes graph connectivity (reference count) to 21-bit integer [0, 2^21-1].
    /// Uses logarithmic scaling to map power-law distributed counts to the quantized linear M-dimension.
    /// </summary>
    public int CalculateGraphConnectivity(long referenceCount)
    {
        if (referenceCount <= 0) return 0;

        // Logarithmic scaling for power-law distribution
        // We want to map reasonable reference counts (0 to ~1B) to [0, 2^21-1]
        // log2(1B) ~= 30. Max value 2^21 is roughly 2 million.
        // We map log2(refCount) * scalar to get meaningful spatial separation.
        
        double logVal = Math.Log2(referenceCount + 1);
        // Scalar: 2,097,151 / 40 (approx max log2 of huge ref counts) ~= 50,000
        int quantized = (int)(logVal * 50_000);
        
        return Math.Clamp(quantized, 0, MaxQuantizedValue);
    }
}
