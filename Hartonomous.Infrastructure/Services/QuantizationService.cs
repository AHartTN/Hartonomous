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
        if (data == null || data.Length == 0)
            return 0;

        // For now, use a simple heuristic based on data patterns
        // This is a placeholder for future graph-based metrics
        // We'll use byte transition frequency as a proxy for connectivity
        
        var transitionCount = 0;
        for (int i = 1; i < data.Length; i++)
        {
            if (data[i] != data[i - 1])
            {
                transitionCount++;
            }
        }

        // Normalize transition frequency to [0, 1]
        // Maximum transitions = length - 1 (alternating pattern)
        var transitionRatio = data.Length > 1 
            ? (double)transitionCount / (data.Length - 1)
            : 0.0;

        // Apply logarithmic scaling for better distribution
        // More transitions suggest higher connectivity potential
        var scaledConnectivity = Math.Log(1 + transitionRatio * 19) / Math.Log(20); // log base 20

        // Scale to [0, MaxQuantizedValue]
        return Math.Clamp(scaledConnectivity, 0.0, 1.0) * MaxQuantizedValue;
    }
}
