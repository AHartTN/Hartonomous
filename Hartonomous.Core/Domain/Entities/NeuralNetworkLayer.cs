using Hartonomous.Core.Domain.Common;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Represents a neural network layer with weights stored as MULTILINESTRINGZM geometry
/// Each neuron's weights stored as a separate linestring in 4D space
/// Enables geometric model versioning and weight distribution analysis
/// </summary>
public sealed class NeuralNetworkLayer : BaseEntity
{
    /// <summary>Foreign key to neural network model</summary>
    public Guid ModelId { get; private set; }
    
    /// <summary>Layer index (0-based) in the network</summary>
    public int LayerIndex { get; private set; }
    
    /// <summary>Layer name (e.g., "dense_1", "conv2d_3", "attention_0")</summary>
    public string LayerName { get; private set; } = null!;
    
    /// <summary>Layer type (e.g., "Dense", "Conv2D", "Attention", "LSTM")</summary>
    public string LayerType { get; private set; } = "Dense";
    
    /// <summary>Weight matrix as MULTILINESTRINGZM (one linestring per neuron)</summary>
    public MultiLineString WeightGeometry { get; private set; } = null!;
    
    /// <summary>Number of neurons (output units) in layer</summary>
    public int NeuronCount { get; private set; }
    
    /// <summary>Input dimensionality (number of weights per neuron)</summary>
    public int InputDim { get; private set; }
    
    /// <summary>Total number of parameters (weights + biases)</summary>
    public long ParameterCount { get; private set; }
    
    /// <summary>Bias vector as LINESTRINGZM (optional)</summary>
    public LineString? BiasGeometry { get; private set; }
    
    /// <summary>Activation function (e.g., "relu", "sigmoid", "tanh", "softmax")</summary>
    public string? ActivationFunction { get; private set; }
    
    /// <summary>Weight initialization method</summary>
    public string? InitializationMethod { get; private set; }
    
    /// <summary>L2 norm of weight matrix (useful for regularization analysis)</summary>
    public double WeightNorm { get; private set; }
    
    /// <summary>Whether weights are frozen (not trainable)</summary>
    public bool IsFrozen { get; private set; }
    
    /// <summary>Training epoch when weights were captured</summary>
    public int? Epoch { get; private set; }
    
    // Navigation property
    // public NeuralNetworkModel Model { get; private set; } = null!;
    
    private NeuralNetworkLayer() { } // EF Core constructor
    
    /// <summary>
    /// Create neural network layer from weight matrix
    /// </summary>
    /// <param name="modelId">ID of the parent model</param>
    /// <param name="layerIndex">Layer position in network (0-based)</param>
    /// <param name="layerName">Descriptive layer name</param>
    /// <param name="layerType">Type of layer (Dense, Conv2D, etc.)</param>
    /// <param name="weights">Weight matrix [neurons x inputs]</param>
    /// <param name="biases">Optional bias vector [neurons]</param>
    /// <param name="activationFunction">Activation function name</param>
    /// <param name="epoch">Training epoch (optional)</param>
    /// <returns>New NeuralNetworkLayer instance</returns>
    public static NeuralNetworkLayer Create(
        Guid modelId,
        int layerIndex,
        string layerName,
        string layerType,
        float[,] weights,
        float[]? biases = null,
        string? activationFunction = null,
        int? epoch = null)
    {
        if (modelId == Guid.Empty)
        {
            throw new ArgumentException("Model ID cannot be empty", nameof(modelId));
        }
        
        if (layerIndex < 0)
        {
            throw new ArgumentException("Layer index cannot be negative", nameof(layerIndex));
        }
        
        if (string.IsNullOrWhiteSpace(layerName))
        {
            throw new ArgumentException("Layer name cannot be null or empty", nameof(layerName));
        }
        
        if (weights == null || weights.Length == 0)
        {
            throw new ArgumentNullException(nameof(weights));
        }
        
        int neurons = weights.GetLength(0);
        int inputDim = weights.GetLength(1);
        
        if (neurons == 0 || inputDim == 0)
        {
            throw new ArgumentException("Weight matrix cannot have zero dimensions", nameof(weights));
        }
        
        if (biases != null && biases.Length != neurons)
        {
            throw new ArgumentException(
                $"Bias vector length ({biases.Length}) must match neuron count ({neurons})",
                nameof(biases));
        }
        
        // Convert weight matrix to MULTILINESTRINGZM
        // Each neuron = one linestring with coordinates: (input_index, weight_value, neuron_index, 0)
        var lineStrings = new List<LineString>();
        double weightSumOfSquares = 0;
        
        for (int n = 0; n < neurons; n++)
        {
            var coords = new CoordinateZM[inputDim];
            
            for (int i = 0; i < inputDim; i++)
            {
                var weight = weights[n, i];
                weightSumOfSquares += weight * weight;
                
                coords[i] = new CoordinateZM(
                    i,              // X: input index
                    weight,         // Y: weight value
                    n,              // Z: neuron index
                    0               // M: reserved for future use (e.g., gradient, importance)
                );
            }
            
            var lineString = new LineString(coords) { SRID = 4326 };
            lineStrings.Add(lineString);
        }
        
        var multiLineString = new MultiLineString(lineStrings.ToArray()) { SRID = 4326 };
        
        // Convert bias vector to LINESTRINGZM (if provided)
        LineString? biasGeometry = null;
        if (biases != null)
        {
            var biasCoords = biases.Select((b, i) => new CoordinateZM(
                i,  // X: neuron index
                b,  // Y: bias value
                0,  // Z: reserved
                0   // M: reserved
            )).ToArray();
            
            biasGeometry = new LineString(biasCoords) { SRID = 4326 };
        }
        
        // Compute weight norm (Frobenius norm)
        double weightNorm = Math.Sqrt(weightSumOfSquares);
        
        // Count parameters
        long paramCount = (long)neurons * inputDim;
        if (biases != null)
        {
            paramCount += neurons;
        }
        
        var now = DateTime.UtcNow;
        
        return new NeuralNetworkLayer
        {
            Id = Guid.NewGuid(),
            ModelId = modelId,
            LayerIndex = layerIndex,
            LayerName = layerName,
            LayerType = layerType,
            WeightGeometry = multiLineString,
            NeuronCount = neurons,
            InputDim = inputDim,
            ParameterCount = paramCount,
            BiasGeometry = biasGeometry,
            ActivationFunction = activationFunction,
            WeightNorm = weightNorm,
            IsFrozen = false,
            Epoch = epoch,
            CreatedAt = now,
            CreatedBy = "System"
        };
    }
    
    /// <summary>
    /// Create layer from double precision weights
    /// </summary>
    public static NeuralNetworkLayer Create(
        Guid modelId,
        int layerIndex,
        string layerName,
        string layerType,
        double[,] weights,
        double[]? biases = null,
        string? activationFunction = null,
        int? epoch = null)
    {
        int neurons = weights.GetLength(0);
        int inputDim = weights.GetLength(1);
        
        var floatWeights = new float[neurons, inputDim];
        for (int n = 0; n < neurons; n++)
        {
            for (int i = 0; i < inputDim; i++)
            {
                floatWeights[n, i] = (float)weights[n, i];
            }
        }
        
        float[]? floatBiases = null;
        if (biases != null)
        {
            floatBiases = biases.Select(b => (float)b).ToArray();
        }
        
        return Create(modelId, layerIndex, layerName, layerType, floatWeights, floatBiases, activationFunction, epoch);
    }
    
    /// <summary>
    /// Extract weight matrix from MULTILINESTRINGZM geometry
    /// </summary>
    /// <returns>Weight matrix [neurons x inputs]</returns>
    public float[,] GetWeights()
    {
        var weights = new float[NeuronCount, InputDim];
        
        for (int n = 0; n < NeuronCount && n < WeightGeometry.Count; n++)
        {
            var lineString = (LineString)WeightGeometry[n];
            
            for (int i = 0; i < lineString.Count && i < InputDim; i++)
            {
                weights[n, i] = (float)lineString.Coordinates[i].Y;
            }
        }
        
        return weights;
    }
    
    /// <summary>
    /// Extract weight matrix as double precision
    /// </summary>
    public double[,] GetWeightsDouble()
    {
        var weights = new double[NeuronCount, InputDim];
        
        for (int n = 0; n < NeuronCount && n < WeightGeometry.Count; n++)
        {
            var lineString = (LineString)WeightGeometry[n];
            
            for (int i = 0; i < lineString.Count && i < InputDim; i++)
            {
                weights[n, i] = lineString.Coordinates[i].Y;
            }
        }
        
        return weights;
    }
    
    /// <summary>
    /// Extract bias vector from LINESTRINGZM geometry
    /// </summary>
    /// <returns>Bias vector [neurons] or null if no biases</returns>
    public float[]? GetBiases()
    {
        if (BiasGeometry == null)
        {
            return null;
        }
        
        return BiasGeometry.Coordinates
            .OrderBy(c => c.X) // Sort by neuron index
            .Select(c => (float)c.Y)
            .ToArray();
    }
    
    /// <summary>
    /// Extract bias vector as double precision
    /// </summary>
    public double[]? GetBiasesDouble()
    {
        if (BiasGeometry == null)
        {
            return null;
        }
        
        return BiasGeometry.Coordinates
            .OrderBy(c => c.X)
            .Select(c => c.Y)
            .ToArray();
    }
    
    /// <summary>
    /// Get weights for a specific neuron
    /// </summary>
    public float[] GetNeuronWeights(int neuronIndex)
    {
        if (neuronIndex < 0 || neuronIndex >= NeuronCount)
        {
            throw new ArgumentOutOfRangeException(nameof(neuronIndex));
        }
        
        var lineString = (LineString)WeightGeometry[neuronIndex];
        return lineString.Coordinates.Select(c => (float)c.Y).ToArray();
    }
    
    /// <summary>
    /// Compute Frobenius norm of weight difference with another layer
    /// </summary>
    public double WeightDistance(NeuralNetworkLayer other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        if (NeuronCount != other.NeuronCount || InputDim != other.InputDim)
        {
            throw new ArgumentException(
                $"Layer dimensions must match: [{NeuronCount}x{InputDim}] != [{other.NeuronCount}x{other.InputDim}]",
                nameof(other));
        }
        
        var thisWeights = GetWeightsDouble();
        var otherWeights = other.GetWeightsDouble();
        
        double sumOfSquaredDiffs = 0;
        for (int n = 0; n < NeuronCount; n++)
        {
            for (int i = 0; i < InputDim; i++)
            {
                double diff = thisWeights[n, i] - otherWeights[n, i];
                sumOfSquaredDiffs += diff * diff;
            }
        }
        
        return Math.Sqrt(sumOfSquaredDiffs);
    }
    
    /// <summary>
    /// Freeze layer weights (mark as not trainable)
    /// </summary>
    public void Freeze()
    {
        IsFrozen = true;
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Unfreeze layer weights (mark as trainable)
    /// </summary>
    public void Unfreeze()
    {
        IsFrozen = false;
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Update weights with new values (e.g., after training)
    /// </summary>
    public void UpdateWeights(float[,] newWeights, float[]? newBiases = null, int? newEpoch = null)
    {
        if (newWeights == null)
        {
            throw new ArgumentNullException(nameof(newWeights));
        }
        
        if (newWeights.GetLength(0) != NeuronCount || newWeights.GetLength(1) != InputDim)
        {
            throw new ArgumentException(
                $"New weights dimensions [{newWeights.GetLength(0)}x{newWeights.GetLength(1)}] " +
                $"must match current dimensions [{NeuronCount}x{InputDim}]",
                nameof(newWeights));
        }
        
        // Rebuild weight geometry
        var lineStrings = new List<LineString>();
        double weightSumOfSquares = 0;
        
        for (int n = 0; n < NeuronCount; n++)
        {
            var coords = new CoordinateZM[InputDim];
            
            for (int i = 0; i < InputDim; i++)
            {
                var weight = newWeights[n, i];
                weightSumOfSquares += weight * weight;
                coords[i] = new CoordinateZM(i, weight, n, 0);
            }
            
            lineStrings.Add(new LineString(coords) { SRID = 4326 });
        }
        
        WeightGeometry = new MultiLineString(lineStrings.ToArray()) { SRID = 4326 };
        WeightNorm = Math.Sqrt(weightSumOfSquares);
        
        // Update biases if provided
        if (newBiases != null)
        {
            if (newBiases.Length != NeuronCount)
            {
                throw new ArgumentException(
                    $"New biases length ({newBiases.Length}) must match neuron count ({NeuronCount})",
                    nameof(newBiases));
            }
            
            var biasCoords = newBiases.Select((b, i) => new CoordinateZM(i, b, 0, 0)).ToArray();
            BiasGeometry = new LineString(biasCoords) { SRID = 4326 };
        }
        
        if (newEpoch.HasValue)
        {
            Epoch = newEpoch.Value;
        }
        
        UpdatedAt = DateTime.UtcNow;
    }
}
