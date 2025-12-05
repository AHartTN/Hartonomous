namespace Hartonomous.Core.Application.Interfaces
{
    public interface IQuantizationService
    {
        /// <summary>
        /// Quantizes input data to produce the Y (Shannon Entropy), Z (Kolmogorov Complexity via Gzip),
        /// and M (Graph Connectivity) dimensions for a POINTZM geometry.
        /// </summary>
        /// <param name="data">The raw data to be quantized.</param>
        /// <returns>A tuple containing (Y, Z, M) values.</returns>
        (double yDimension, double zDimension, double mDimension) Quantize(byte[] data);

        /// <summary>
        /// Calculates the Shannon Entropy (Y dimension) for the given byte array.
        /// </summary>
        /// <param name="data">The byte array for which to calculate entropy.</param>
        /// <returns>The Shannon Entropy value.</returns>
        double CalculateShannonEntropy(byte[] data);

        /// <summary>
        /// Calculates an approximation of Kolmogorov Complexity (Z dimension) using Gzip compression ratio.
        /// </summary>
        /// <param name="data">The byte array for which to calculate Kolmogorov Complexity.</param>
        /// <returns>The Kolmogorov Complexity approximation value.</returns>
        double CalculateKolmogorovComplexity(byte[] data);

        /// <summary>
        /// Calculates the Graph Connectivity (M dimension) for the given data.
        /// This is a placeholder and will need a more concrete implementation based on how
        /// data relationships are established. For now, it might represent a simple reference count
        /// or a more complex graph-based metric.
        /// </summary>
        /// <param name="data">The byte array representing the data for which to calculate connectivity.</param>
        /// <returns>The Graph Connectivity value.</returns>
        double CalculateGraphConnectivity(byte[] data);

        /// <summary>
        /// Calculates the Graph Connectivity (M dimension) based on the reference count.
        /// Uses logarithmic scaling to map power-law distributed counts to the quantized linear M-dimension.
        /// </summary>
        /// <param name="referenceCount">The number of references to the constant.</param>
        /// <returns>The quantized M dimension value [0, 2^21-1].</returns>
        int CalculateGraphConnectivity(long referenceCount);
    }
}
